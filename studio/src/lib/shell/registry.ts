import type { Component } from 'svelte';

export type ToolStatus = 'ready' | 'planned';

/** One tool of the shell. Planned tools show greyed in the dock and never open. */
export interface ToolDef {
  id: string;
  name: string;
  /** One line, shown in the dock and the catalogue. */
  blurb: string;
  /** Inline SVG. */
  icon: string;
  status: ToolStatus;
  /** 'single': opening it again brings its tab to the front. 'many': every open makes a new tab. */
  instances: 'single' | 'many';
  /** Defaults to true; Home is the only tab that cannot close. */
  closable?: boolean;
  /** Defaults to true; Home and the mod editor are not in the dock. */
  inDock?: boolean;
  /** Dynamic import, so a tool's code (e.g. the 3D viewer) is only fetched when it is first opened. */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  load?: () => Promise<{ default: Component<any> }>;
}

/** The tools, in registration order (the dock's order). Ported from Scute's shell/registry.ts. */
export class Registry {
  private readonly tools: ToolDef[] = [];

  register(def: ToolDef): void {
    if (this.tools.some((t) => t.id === def.id)) throw new Error(`tool "${def.id}" is already registered`);
    this.tools.push(def);
  }

  get(id: string): ToolDef | undefined {
    return this.tools.find((t) => t.id === id);
  }

  all(): ToolDef[] {
    return [...this.tools];
  }

  dock(): ToolDef[] {
    return this.tools.filter((t) => t.inDock !== false);
  }

  /** Planned tools are shown so they can be clicked; clicking must do nothing. */
  isOpenable(id: string): boolean {
    const tool = this.get(id);
    return tool?.status === 'ready' && tool.load !== undefined;
  }
}
