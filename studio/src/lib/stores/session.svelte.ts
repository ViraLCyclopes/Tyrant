import { getContext, setContext } from 'svelte';
import { RpcError, asRpcError, type JobMethod, type ParamsOf, type Rpc } from '$lib/rpc/client';
import type { DumpRunResult, InstallInfo, RefreshAllResult, RpcJobs, WorkspaceStatus } from '$lib/rpc/types.gen';
import type { Platform } from '$lib/platform';
import type { KeyValueStore } from '$lib/storage';
import { toLevel } from '$lib/shell/log';
import { LogStore } from '$lib/shell/logStore.svelte';
import type { Tab } from '$lib/shell/tab.svelte';

export const SESSION_KEY = Symbol('tyrant-session');
const LAST_WORKSPACE = 'tyrant.lastWorkspace';
const RECENT_WORKSPACES = 'tyrant.recentWorkspaces';
const MAX_RECENT = 5;

export const START_GAME_WARNING =
  'This starts Prehistoric Kingdom through Steam, reads its game data at the main menu and closes it again (about a minute). Continue?';

export interface ActiveJob {
  id: string | null;
  title: string;
  fraction: number;
  message: string;
  cancel: (() => Promise<void>) | null;
}

/** App state shared by every tab: the game, the open workspace, the running job and the log. */
export class Session {
  install = $state<InstallInfo | null>(null);
  workspace = $state<WorkspaceStatus | null>(null);
  /** A session-level error (core stopped, workspace could not open), shown above every tab. Tabs show their own. */
  error = $state<RpcError | null>(null);
  readonly log = new LogStore();
  job = $state<ActiveJob | null>(null);
  recent = $state<string[]>([]);
  ready = $state(false);
  /** The workspace action that failed last, so an error's fix button repeats it on the right folder. */
  private lastFailed: { kind: 'open' | 'create'; dir: string } | null = null;

  constructor(
    readonly rpc: Rpc,
    readonly platform: Platform,
    readonly store: KeyValueStore,
  ) {
    this.recent = readList(store.get(RECENT_WORKSPACES));
    rpc.onLog((n) => this.log.add({ level: toLevel(n.level), message: n.message, tab: null }));
    rpc.onCoreExit((info) => {
      this.job = null;
      const message =
        info.error ??
        (info.restarting
          ? 'The Tyrant core stopped unexpectedly and was restarted; the running task was stopped.'
          : 'The Tyrant core stopped and could not be restarted. Restart Tyrant.');
      this.error = new RpcError(message, 'SIDECAR_EXITED');
    });
    rpc.onCoreStarted(() => {
      const dir = this.workspace?.dir;
      if (dir) void this.reopen(dir);
    });
  }

  /** A message for the tab that asked (or every tab when none did, e.g. after a reload). */
  private tell(message: string, tab?: Tab): void {
    if (tab) tab.info(message);
    else this.log.add({ level: 'info', message, tab: null });
  }

  private report(error: RpcError, tab?: Tab): void {
    if (tab) tab.fail(error);
    else this.error = error;
  }

  get busy(): boolean {
    return this.job !== null;
  }

  /** For background loads (table queries, lookups): keeps the error the user is reading and reports only its own failure. */
  async quietly<T>(work: () => Promise<T>): Promise<T | null> {
    try {
      return await work();
    } catch (e) {
      this.error = asRpcError(e);
      return null;
    }
  }

  /** Runs a user action: clears the previous error, shows a new one, returns null on failure. */
  async safely<T>(work: () => Promise<T>): Promise<T | null> {
    this.error = null;
    try {
      return await work();
    } catch (e) {
      this.error = asRpcError(e);
      return null;
    }
  }

  /** First run: reopen the last workspace; otherwise find the game so a workspace can be created. */
  async start(): Promise<void> {
    const last = this.store.get(LAST_WORKSPACE);
    if (last && (await this.openWorkspace(last))) {
      this.ready = true;
      void this.resumeRunningJob();
      return;
    }
    // Forget it only when the folder is no longer a workspace; a missing core or a moved game is recoverable.
    if (last && this.error?.code === 'WORKSPACE_INVALID') this.store.remove(LAST_WORKSPACE);
    try {
      this.install = await this.rpc.call('install.detect', {});
    } catch (e) {
      this.error ??= asRpcError(e); // keep the more useful "could not open the last workspace" error
    }
    this.ready = true;
  }

  async detectGame(gamePath?: string): Promise<boolean> {
    const info = await this.safely(() => this.rpc.call('install.detect', { gamePath: gamePath ?? null }));
    if (info) this.install = info;
    return info !== null;
  }

  async openWorkspace(dir: string, gamePath?: string): Promise<boolean> {
    const status = await this.safely(() => this.rpc.call('workspace.open', { dir, gamePath: gamePath ?? null }));
    this.lastFailed = status ? null : { kind: 'open', dir };
    if (status) this.useWorkspace(status);
    else if (this.error?.code === 'WORKSPACE_INVALID') this.forgetRecent(dir); // the folder is gone or no longer a workspace
    return status !== null;
  }

  private forgetRecent(dir: string) {
    const kept = this.recent.filter((d) => d.toLowerCase() !== dir.toLowerCase());
    if (kept.length === this.recent.length) return;
    this.recent = kept;
    this.store.set(RECENT_WORKSPACES, JSON.stringify(this.recent));
  }

  async createWorkspace(dir: string): Promise<boolean> {
    const status = await this.safely(() => this.rpc.call('workspace.create', { dir, gamePath: this.install?.rootDir ?? null }));
    this.lastFailed = status ? null : { kind: 'create', dir };
    if (status) this.useWorkspace(status);
    return status !== null;
  }

  async refreshStatus(): Promise<void> {
    if (!this.workspace) return;
    try {
      this.workspace = await this.rpc.call('workspace.status');
    } catch {
      // keep whatever error the action that just finished reported
    }
  }

  /**
   * Runs a job with progress in the status line; its start, each new progress message and any error go to the tab that
   * started it. Returns the result, or null if it failed (error reported) or was cancelled.
   */
  async runJob<M extends JobMethod>(method: M, params: ParamsOf<M>, title: string, tab?: Tab): Promise<RpcJobs[M] | null> {
    if (this.job) return null;
    if (tab) tab.error = null;
    else this.error = null;
    this.job = { id: null, title, fraction: 0, message: 'Starting…', cancel: null };
    this.tell(`${title} started.`, tab);
    let lastMessage = '';
    try {
      const handle = await this.rpc.job(method, params, (fraction, message) => {
        if (message && message !== lastMessage) {
          lastMessage = message;
          this.tell(message, tab);
        }
        if (!this.job) return;
        this.job.fraction = fraction;
        this.job.message = message;
      });
      if (this.job) {
        this.job.id = handle.id;
        this.job.cancel = () => handle.cancel();
      }
      return await handle.done;
    } catch (e) {
      const error = asRpcError(e);
      if (error.code === 'CANCELLED') this.tell(`${title} was cancelled.`, tab);
      else if (error.code !== 'SIDECAR_EXITED') this.report(error, tab); // a core exit was already explained
      return null;
    } finally {
      this.job = null;
      await this.refreshStatus();
    }
  }

  /** After a window reload the core may still be running a job: show it again (progress, Cancel) until it ends. */
  async resumeRunningJob(): Promise<void> {
    if (this.job) return;
    const current = (await this.quietly(() => this.rpc.call('job.current')))?.job;
    if (!current || this.job) return;
    this.job = { id: current.jobId, title: current.title, fraction: current.fraction, message: current.message, cancel: null };
    const handle = this.rpc.attachJob(current.jobId, (fraction, message) => {
      if (!this.job) return;
      this.job.fraction = fraction;
      this.job.message = message;
    });
    this.job.cancel = () => handle.cancel();
    try {
      await handle.done;
      this.tell(`${current.title} finished.`);
    } catch (e) {
      const error = asRpcError(e);
      if (error.code === 'CANCELLED') this.tell(`${current.title} was cancelled.`);
      else if (error.code !== 'SIDECAR_EXITED') this.report(error);
    } finally {
      this.job = null;
      await this.refreshStatus();
    }
  }

  async cancelJob(): Promise<void> {
    await this.job?.cancel?.();
  }

  async refreshAll(tab?: Tab): Promise<RefreshAllResult | null> {
    if (this.workspace?.dumper === 'installed' && !(await this.platform.confirm(START_GAME_WARNING, 'Refresh all'))) return null;
    return this.runJob('workspace.refreshAll', undefined, 'Refresh all', tab);
  }

  async runDump(tab?: Tab): Promise<DumpRunResult | null> {
    if (!(await this.platform.confirm(START_GAME_WARNING, 'Run data dump'))) return null;
    return this.runJob('dump.run', { timeoutSeconds: 300 }, 'Data dump', tab);
  }

  /** Removes Tyrant from the game after asking: this also deletes every installed mod, some of which may exist nowhere else. */
  async uninstallDumper(tab?: Tab): Promise<void> {
    let installed: { state: string }[] = [];
    try {
      installed = (await this.rpc.call('mods.list')).mods.filter((m) => m.state !== 'notInstalled');
    } catch {
      // no workspace mods to describe; the question below still covers what is removed
    }
    const gameOnly = installed.filter((m) => m.state === 'gameOnly').length;
    const message = [
      "Remove Tyrant from the game? This removes Tyrant's dumper and framework, and MelonLoader if Tyrant installed it and no other mods use it.",
      installed.length
        ? `It also removes ${installed.length} installed mod(s)${gameOnly ? `; ${gameOnly} of them ${gameOnly === 1 ? 'is' : 'are'} not in this workspace, so Tyrant cannot reinstall ${gameOnly === 1 ? 'it' : 'them'}` : ''}.`
        : '',
      'Mods in your workspace stay; you can install them again.',
    ].filter(Boolean).join(' ');
    if (!(await this.platform.confirm(message, 'Uninstall from game'))) return;
    const result = tab ? await tab.safely(() => this.rpc.call('dump.uninstall')) : await this.safely(() => this.rpc.call('dump.uninstall'));
    if (result) this.tell(result.message, tab);
    await this.refreshStatus();
  }

  async copyDiagnostics(tab?: Tab): Promise<void> {
    const result = tab ? await tab.safely(() => this.rpc.call('app.diagnostics')) : await this.safely(() => this.rpc.call('app.diagnostics'));
    if (!result) return;
    await this.platform.copy(result.text);
    this.tell('Diagnostics copied to the clipboard; paste them into your bug report.', tab);
  }

  /** Handles the fix button of an error (raised by `tab`'s actions when given). */
  async applyFix(fix: string, tab?: Tab): Promise<void> {
    if (tab) tab.error = null;
    switch (fix) {
      case 'PICK_GAME_FOLDER': {
        const dir = await this.platform.pickFolder('Select the Prehistoric Kingdom folder');
        if (!dir) return;
        // Re-point the workspace whose open failed, not the one that happens to be open.
        const failedOpen = this.lastFailed?.kind === 'open' ? this.lastFailed.dir : null;
        const workspace = failedOpen ?? this.workspace?.dir;
        if (workspace) await this.openWorkspace(workspace, dir);
        else await this.detectGame(dir);
        return;
      }
      case 'PICK_WORKSPACE_FOLDER': {
        const creating = this.lastFailed?.kind === 'create';
        const dir = await this.platform.pickFolder(creating ? 'Choose an empty folder for the new workspace' : 'Open a workspace folder');
        if (!dir) return;
        if (creating) await this.createWorkspace(dir);
        else await this.openWorkspace(dir);
        return;
      }
      case 'REFRESH_WORKSPACE':
        await this.refreshAll(tab);
        return;
      case 'INSTALL_DUMPER':
        await this.runJob('dump.install', undefined, 'Install dumper', tab);
        return;
    }
  }

  private useWorkspace(status: WorkspaceStatus): void {
    this.workspace = status;
    this.install = { rootDir: status.gameRoot, steamAppId: status.steamAppId, buildGuid: status.buildGuid };
    this.store.set(LAST_WORKSPACE, status.dir);
    this.recent = [status.dir, ...this.recent.filter((d) => d.toLowerCase() !== status.dir.toLowerCase())].slice(0, MAX_RECENT);
    this.store.set(RECENT_WORKSPACES, JSON.stringify(this.recent));
    this.openPreviews(status.dir);
  }

  /** Previews are files in <workspace>\cache\previews; the shell only serves that folder once asked. */
  private openPreviews(dir: string): void {
    void this.platform.allowPreviews(`${dir}\\cache\\previews`).catch(() => {
      // without it previews show as broken images; the rest of the app is unaffected
    });
  }

  /** After a core restart the new process has no workspace open; reopen it without hiding the restart message. */
  private async reopen(dir: string): Promise<void> {
    try {
      this.workspace = await this.rpc.call('workspace.open', { dir });
      this.openPreviews(dir);
    } catch (e) {
      this.error = asRpcError(e);
    }
  }
}

function readList(raw: string | null): string[] {
  try {
    const value: unknown = JSON.parse(raw ?? '[]');
    return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [];
  } catch {
    return [];
  }
}

export function setSession(session: Session): void {
  setContext(SESSION_KEY, session);
}

export function getSession(): Session {
  return getContext<Session>(SESSION_KEY);
}
