import { asRpcError, type RpcError, type Rpc } from '$lib/rpc/client';
import type { ModCheckReport, ModDetail } from '$lib/rpc/types.gen';
import type { Tab } from '$lib/shell/tab.svelte';

export type EditMethod =
  | 'mods.setDetails'
  | 'mods.renameSkin'
  | 'mods.removeSkin'
  | 'mods.setColors'
  | 'mods.setSkinFile'
  | 'mods.setThumbnail'
  | 'mods.removeReplacement';

const CHECK_DELAY = 1000;
const MAX_UNDO = 100;

/** One mod open in an editor tab: its content, Check results, and undo/redo by whole mod.json snapshots. */
export class ModDoc {
  detail = $state.raw<ModDetail | null>(null);
  check = $state.raw<ModCheckReport | null>(null);
  canUndo = $state(false);
  canRedo = $state(false);
  private undoStack: string[] = [];
  private redoStack: string[] = [];
  private checkGeneration = 0;

  constructor(
    readonly id: string,
    private readonly rpc: Rpc,
    private readonly tab: Tab,
    private readonly delay: (fn: () => void, ms: number) => unknown = (fn, ms) => setTimeout(fn, ms),
  ) {}

  async load(): Promise<boolean> {
    const detail = await this.tab.quietly(() => this.rpc.call('mods.get', { id: this.id }));
    if (!detail) return false;
    this.detail = detail;
    this.scheduleCheck(0);
    return true;
  }

  /** Saves one change at once; on success the previous mod.json becomes an undo step. */
  async edit(method: EditMethod, params: Record<string, unknown>): Promise<boolean> {
    const before = this.detail;
    if (!before) return false;
    try {
      const next = (await this.rpc.call(method, { id: this.id, revision: before.revision, ...params } as never)) as ModDetail;
      this.push(this.undoStack, before.manifestJson);
      this.redoStack = [];
      this.detail = next;
      this.sync();
      this.scheduleCheck(CHECK_DELAY);
      return true;
    } catch (e) {
      await this.failed(asRpcError(e));
      return false;
    }
  }

  async undo(): Promise<void> {
    await this.restore(this.undoStack, this.redoStack);
  }

  async redo(): Promise<void> {
    await this.restore(this.redoStack, this.undoStack);
  }

  /** The tab was shown again: if mod.json changed meanwhile, take the new content and forget undo (it would overwrite it). */
  async refreshIfChanged(): Promise<void> {
    if (!this.detail) return;
    const latest = await this.tab.quietly(() => this.rpc.call('mods.get', { id: this.id }));
    if (latest && latest.revision !== this.detail.revision) this.outsideChange(latest);
  }

  /** Re-reads the mod after an action that changed files but not through edit() (Replace file…, Restore cutouts, Add skin). */
  async reload(): Promise<void> {
    const latest = await this.tab.quietly(() => this.rpc.call('mods.get', { id: this.id }));
    if (latest) {
      this.detail = latest;
      this.scheduleCheck(CHECK_DELAY);
    }
  }

  async runCheck(): Promise<void> {
    const generation = ++this.checkGeneration;
    const report = await this.tab.quietly(() => this.rpc.call('mods.check', { id: this.id }));
    if (report && generation === this.checkGeneration) this.check = report;
  }

  private scheduleCheck(ms: number): void {
    const generation = ++this.checkGeneration;
    this.delay(() => {
      if (generation === this.checkGeneration) void this.runCheck();
    }, ms);
  }

  private async restore(from: string[], to: string[]): Promise<void> {
    const current = this.detail;
    const target = from.at(-1);
    if (!current || target === undefined) return;
    try {
      const next = await this.rpc.call('mods.saveManifest', { id: this.id, revision: current.revision, manifest: target });
      from.pop();
      this.push(to, current.manifestJson);
      this.detail = next;
      this.sync();
      this.scheduleCheck(CHECK_DELAY);
    } catch (e) {
      await this.failed(asRpcError(e));
    }
  }

  private async failed(error: RpcError): Promise<void> {
    if (error.code === 'MOD_CHANGED') {
      const latest = await this.tab.quietly(() => this.rpc.call('mods.get', { id: this.id }));
      if (latest) this.outsideChange(latest);
      return;
    }
    this.tab.fail(error);
  }

  private outsideChange(latest: ModDetail): void {
    this.detail = latest;
    this.undoStack = [];
    this.redoStack = [];
    this.sync();
    this.tab.info('mod.json changed outside this tab; reloaded.');
    this.scheduleCheck(0);
  }

  private push(stack: string[], manifest: string): void {
    stack.push(manifest);
    if (stack.length > MAX_UNDO) stack.shift();
  }

  private sync(): void {
    this.canUndo = this.undoStack.length > 0;
    this.canRedo = this.redoStack.length > 0;
  }
}
