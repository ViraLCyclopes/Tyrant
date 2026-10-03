import { getContext, setContext } from 'svelte';
import { asRpcError, type RpcError } from '$lib/rpc/client';
import type { LogStore } from './logStore.svelte';
import type { Menu } from './menu';

export const TAB_KEY = Symbol('tyrant-tab');

export interface OpenOptions {
  title?: string;
  key?: string;
}

/** What a tab may ask of the shell around it. */
export interface TabHost {
  retitle(tabId: string, title: string): void;
  openTool(toolId: string, options?: OpenOptions): string | null;
}

/** One open tab, as its tool sees it: its log, its error banner, whether it is showing. */
export class Tab {
  active = $state(false);
  /** An actionable error (one with a fix) raised by this tab's own actions. */
  error = $state<RpcError | null>(null);
  /** Extra menus this tab's tool adds to the menu bar while it is showing (plan 2's Mod menu). */
  menus = $state<Menu[]>([]);
  /** False once the tab is closed: late results of its work go to every tab instead. */
  private attached = true;

  constructor(
    readonly id: string,
    private readonly log: LogStore,
    private readonly host: TabHost,
    /** Where an error goes once this tab is closed (no banner to show it in): the session's banner. */
    private readonly onOrphanError?: (error: RpcError) => void,
  ) {}

  private get owner(): string | null {
    return this.attached ? this.id : null;
  }

  info(message: string, detail?: string): void {
    this.log.add({ level: 'info', message, detail, tab: this.owner });
  }

  warn(message: string, detail?: string): void {
    this.log.add({ level: 'warn', message, detail, tab: this.owner });
  }

  /** Every error is logged; one with a fix action also becomes this tab's banner. */
  fail(error: RpcError): void {
    this.log.add({ level: 'error', message: error.message, detail: error.code, tab: this.owner });
    if (!this.attached) this.onOrphanError?.(error);
    else if (error.fix) this.error = error;
  }

  /** The tab closed: anything its still-running work reports goes to every tab. */
  detach(): void {
    this.attached = false;
    this.error = null;
  }

  /** A user action: clears this tab's banner, reports a failure, returns null on failure. */
  async safely<T>(work: () => Promise<T>): Promise<T | null> {
    this.error = null;
    return this.quietly(work);
  }

  /** A background load: keeps the banner the user is reading and reports only its own failure. */
  async quietly<T>(work: () => Promise<T>): Promise<T | null> {
    try {
      return await work();
    } catch (e) {
      this.fail(asRpcError(e));
      return null;
    }
  }

  setTitle(title: string): void {
    this.host.retitle(this.id, title);
  }

  openTool(toolId: string, options?: OpenOptions): string | null {
    return this.host.openTool(toolId, options);
  }
}

export function setTab(tab: Tab): void {
  setContext(TAB_KEY, tab);
}

export function getTab(): Tab {
  const tab = getContext<Tab | undefined>(TAB_KEY);
  if (!tab) throw new Error('getTab() used outside a tab');
  return tab;
}
