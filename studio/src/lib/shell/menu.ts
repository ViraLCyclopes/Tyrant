/** One row in a dropdown. Ported from Scute's shell/menubar.ts (the model only; the bar is MenuBar.svelte). */
export type MenuItem =
  | { separator: true }
  | {
      separator?: false;
      label: string;
      /** Shown right-aligned; binding the key is the shell's job. */
      shortcut?: string;
      run(): void;
      /** Present makes the item checkable. */
      checked?(): boolean;
      /** Defaults to enabled. A disabled item still shows, so its shortcut stays discoverable. */
      enabled?(): boolean;
    };

export interface Menu {
  label: string;
  /** Re-read every time the menu opens, so rows that depend on state stay current. */
  items: MenuItem[] | (() => MenuItem[]);
}

export function itemsOf(menu: Menu): MenuItem[] {
  return typeof menu.items === 'function' ? menu.items() : menu.items;
}

export function isEnabled(item: MenuItem): boolean {
  if (item.separator) return false;
  return item.enabled ? item.enabled() : true;
}
