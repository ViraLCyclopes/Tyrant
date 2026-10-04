import { getContext } from 'svelte';
import { SvelteMap } from 'svelte/reactivity';
import type { KeyValueStore } from '$lib/storage';
import type { RpcError } from '$lib/rpc/client';
import { attentionFor, type LogRecord } from './log';
import type { LogStore } from './logStore.svelte';
import type { Registry } from './registry';
import { Tab, type OpenOptions, type TabHost } from './tab.svelte';
import { TabStore, type TabRecord } from './tabs';

export interface ShellPrefs {
  reopenTabs: boolean;
  logPosition: 'bottom' | 'side';
}

/** A tab's log panel: open or folded, and its width at the side and height at the bottom (Scute's 360 / 220). */
export interface PanelState {
  open: boolean;
  side: number;
  bottom: number;
}

interface SavedTabs {
  tabs: { toolId: string; title: string; key: string | null }[];
  active: number;
}

const TABS_KEY = 'tyrant.shell.tabs';
const PREFS_KEY = 'tyrant.shell.prefs';
const INTRO_KEY = 'tyrant.home.introHidden';
export const SHELL_KEY = Symbol('tyrant-shell');
const DEFAULT_PREFS: ShellPrefs = { reopenTabs: true, logPosition: 'side' };
const DEFAULT_PANEL: PanelState = { open: false, side: 360, bottom: 220 };

/** The shell's state: which tabs are open, each tab's Tab object, log panels, markers, preferences. No DOM. */
export class ShellState implements TabHost {
  prefs = $state<ShellPrefs>({ ...DEFAULT_PREFS });
  /** The Home intro card was hidden (Home and Preferences both change it). */
  introHidden = $state(false);
  private version = $state(0);
  private readonly store = new TabStore();
  private readonly tabObjects = new Map<string, Tab>();
  private readonly panels = new SvelteMap<string, PanelState>();
  /** Per tab: the last log record seen while it was showing; later warnings and errors mark the tab. */
  private readonly seen = new SvelteMap<string, number>();
  /** Per tab: the last log record seen with its log panel open; later warnings and errors light the Log button's dot. */
  private readonly logSeen = new SvelteMap<string, number>();

  constructor(
    readonly registry: Registry,
    private readonly log: LogStore,
    private readonly storage: KeyValueStore,
    /** Errors reported by a tab after it was closed (its job failed later): shown above every tab. */
    private readonly onOrphanError?: (error: RpcError) => void,
  ) {
    this.store.subscribe(() => {
      this.syncTabs();
      this.version++;
    });
    log.onAdd((record) => {
      if (record.tab === null) return;
      if (this.panel(record.tab).open) this.logSeen.set(record.tab, record.seq);
      if (record.tab !== this.store.activeId()) return;
      this.seen.set(record.tab, record.seq);
      // An error is never hidden behind a folded log; a warning lights the Log button's dot (and shows in the status line).
      if (record.level === 'error') this.setPanel(record.tab, { open: true });
    });
  }

  get tabs(): TabRecord[] {
    void this.version;
    return this.store.list();
  }

  get activeId(): string | null {
    void this.version;
    return this.store.activeId();
  }

  tab(id: string): Tab {
    const tab = this.tabObjects.get(id);
    if (!tab) throw new Error(`no tab ${id}`);
    return tab;
  }

  title(id: string): string {
    void this.version;
    return this.store.displayTitle(id);
  }

  /** Opens a tool, or brings its tab to the front (single tools, and keyed opens). Null when it cannot open. */
  openTool(toolId: string, options: OpenOptions = {}): string | null {
    const def = this.registry.get(toolId);
    if (!def || !this.registry.isOpenable(toolId)) return null;
    const key = options.key ?? null;
    if (def.instances === 'single' || key !== null) {
      const existing = this.store.list().find((t) => t.toolId === toolId && t.key === key);
      if (existing) {
        this.store.activate(existing.id);
        return existing.id;
      }
    }
    return this.store.open({ toolId, title: options.title ?? def.name, closable: def.closable ?? true, key });
  }

  close(id: string): void {
    const tab = this.tabObjects.get(id);
    if (!this.store.close(id)) return;
    tab?.detach(); // a job it started may still finish: its result then goes to every tab
    this.tabObjects.delete(id);
    this.panels.delete(id);
    this.logSeen.delete(id);
    this.seen.delete(id);
    this.log.clear(id);
    this.save();
  }

  activate(id: string): void {
    this.store.activate(id);
  }

  cycle(delta: 1 | -1): void {
    this.store.cycle(delta);
  }

  retitle(id: string, title: string): void {
    this.store.retitle(id, title);
  }

  /** ⚠ / ✕ for a tab that logged a warning or error since it was last shown; never for the shown tab. */
  marker(id: string): 'warn' | 'error' | null {
    if (id === this.activeId) return null;
    return this.unseenProblem(id);
  }

  /** The loudest warning or error the tab logged since it was last shown. */
  private unseenProblem(id: string): 'warn' | 'error' | null {
    const after = this.seen.get(id) ?? 0;
    return attentionFor(this.log.records.filter((r) => r.tab === id && r.seq > after)).get(id) ?? null;
  }

  /** The shown tab's latest own message, for the status line. */
  status(id: string): LogRecord | null {
    for (let i = this.log.records.length - 1; i >= 0; i--) if (this.log.records[i].tab === id) return this.log.records[i];
    return null;
  }

  panel(id: string): PanelState {
    return this.panels.get(id) ?? DEFAULT_PANEL;
  }

  setPanel(id: string, patch: Partial<PanelState>): void {
    this.panels.set(id, { ...this.panel(id), ...patch });
    if (patch.open) this.logSeen.set(id, this.log.lastSeq());
  }

  /** The Log button's dot: a warning or error the tab logged since its log was last open. */
  logDot(id: string): 'warn' | 'error' | null {
    if (this.panel(id).open) return null;
    const after = this.logSeen.get(id) ?? 0;
    return attentionFor(this.log.records.filter((r) => r.tab === id && r.seq > after)).get(id) ?? null;
  }

  toggleLog(): void {
    const id = this.activeId;
    if (id) this.setPanel(id, { open: !this.panel(id).open });
  }

  setPrefs(patch: Partial<ShellPrefs>): void {
    this.prefs = { ...this.prefs, ...patch };
    this.storage.set(PREFS_KEY, JSON.stringify(this.prefs));
  }

  setIntroHidden(hidden: boolean): void {
    this.introHidden = hidden;
    this.storage.set(INTRO_KEY, hidden ? '1' : '0');
  }

  /** Opens Home, then (if wanted) the tabs of the last session. */
  start(): void {
    this.prefs = readPrefs(this.storage.get(PREFS_KEY));
    this.introHidden = this.storage.get(INTRO_KEY) === '1';
    this.openTool('home');
    if (!this.prefs.reopenTabs) return;
    const saved = readTabs(this.storage.get(TABS_KEY));
    const opened = saved.tabs.map((t) => (t.toolId === 'home' ? null : this.openTool(t.toolId, { title: t.title, key: t.key ?? undefined })));
    const active = opened[saved.active] ?? opened.filter((id): id is string => id !== null).at(-1) ?? null;
    if (active) this.store.activate(active);
  }

  save(): void {
    const list = this.store.list();
    const saved: SavedTabs = {
      tabs: list.map((t) => ({ toolId: t.toolId, title: t.title, key: t.key })),
      active: list.findIndex((t) => t.id === this.store.activeId()),
    };
    this.storage.set(TABS_KEY, JSON.stringify(saved));
  }

  /** Every tab has its Tab object; only the shown one is active, and showing a tab clears its marker. */
  private syncTabs(): void {
    const active = this.store.activeId();
    for (const record of this.store.list()) {
      if (this.tabObjects.has(record.id)) continue;
      const tab = new Tab(record.id, this.log, this, this.onOrphanError);
      tab.key = record.key;
      this.tabObjects.set(record.id, tab);
    }
    for (const [id, tab] of this.tabObjects) tab.active = id === active;
    if (!active) return;
    if (this.unseenProblem(active) === 'error') this.setPanel(active, { open: true }); // show what the ✕ was about
    this.seen.set(active, this.log.lastSeq());
  }
}

function readPrefs(raw: string | null): ShellPrefs {
  try {
    const value = JSON.parse(raw ?? '{}') as Partial<ShellPrefs> | null;
    if (!value || typeof value !== 'object' || Array.isArray(value)) return { ...DEFAULT_PREFS };
    return {
      reopenTabs: typeof value.reopenTabs === 'boolean' ? value.reopenTabs : DEFAULT_PREFS.reopenTabs,
      logPosition: value.logPosition === 'bottom' ? 'bottom' : 'side',
    };
  } catch {
    return { ...DEFAULT_PREFS };
  }
}

function readTabs(raw: string | null): SavedTabs {
  try {
    const value = JSON.parse(raw ?? '{}') as { tabs?: unknown; active?: unknown } | null;
    const tabs = Array.isArray(value?.tabs)
      ? value.tabs.filter(
          (t): t is SavedTabs['tabs'][number] =>
            typeof t?.toolId === 'string' && typeof t?.title === 'string' && (t.key === null || typeof t.key === 'string'),
        )
      : [];
    return { tabs, active: typeof value?.active === 'number' ? value.active : -1 };
  } catch {
    return { tabs: [], active: -1 };
  }
}

/** The shell, for tools that need it beyond their tab (Home's dock and intro). */
export function getShell(): ShellState {
  return getContext<ShellState>(SHELL_KEY);
}
