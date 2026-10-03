export interface TabRecord {
  id: string;
  toolId: string;
  title: string;
  closable: boolean;
  /** Identifies a keyed tab (e.g. a mod editor's mod id), so opening it again finds it. */
  key: string | null;
}

export interface TabInit {
  toolId: string;
  title: string;
  closable?: boolean;
  key?: string | null;
}

/**
 * The open tabs and which one is showing. No DOM and no components (the shell keeps those), so the rules are testable.
 * Ported from Scute's shell/tabs.ts.
 */
export class TabStore {
  private tabs: TabRecord[] = [];
  private active: string | null = null;
  private counter = 0;
  private readonly listeners = new Set<() => void>();

  list(): TabRecord[] {
    return [...this.tabs];
  }

  get(id: string): TabRecord | undefined {
    return this.tabs.find((t) => t.id === id);
  }

  activeId(): string | null {
    return this.active;
  }

  open(init: TabInit): string {
    this.counter += 1;
    const tab: TabRecord = { id: `tab-${this.counter}`, toolId: init.toolId, title: init.title, closable: init.closable ?? true, key: init.key ?? null };
    this.tabs.push(tab);
    this.active = tab.id;
    this.emit();
    return tab.id;
  }

  /** Closes a closable tab; false when it is not closable or unknown. Only closing the active tab moves the user (to the left neighbour). */
  close(id: string): boolean {
    const index = this.tabs.findIndex((t) => t.id === id);
    if (index === -1 || !this.tabs[index].closable) return false;
    this.tabs.splice(index, 1);
    if (this.active === id) {
      const neighbour = this.tabs[index - 1] ?? this.tabs[0] ?? null;
      this.active = neighbour ? neighbour.id : null;
    }
    this.emit();
    return true;
  }

  activate(id: string): void {
    if (!this.tabs.some((t) => t.id === id) || this.active === id) return;
    this.active = id;
    this.emit();
  }

  retitle(id: string, title: string): void {
    const tab = this.get(id);
    if (!tab || tab.title === title) return;
    tab.title = title;
    this.emit();
  }

  /** Tabs sharing a title are numbered in opening order; the first keeps the bare name. */
  displayTitle(id: string): string {
    const tab = this.get(id);
    if (!tab) return '';
    const same = this.tabs.filter((t) => t.title === tab.title);
    if (same.length < 2) return tab.title;
    const position = same.findIndex((t) => t.id === id);
    return position === 0 ? tab.title : `${tab.title} (${position + 1})`;
  }

  /** Ctrl+Tab / Ctrl+Shift+Tab: the next or previous tab, wrapping around. */
  cycle(delta: 1 | -1): void {
    if (this.tabs.length === 0) return;
    const index = this.tabs.findIndex((t) => t.id === this.active);
    const next = this.tabs[(index + delta + this.tabs.length) % this.tabs.length];
    this.activate(next.id);
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  private emit(): void {
    for (const listener of this.listeners) listener();
  }
}
