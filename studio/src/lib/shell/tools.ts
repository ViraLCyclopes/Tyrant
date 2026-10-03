import type { Registry } from './registry';
import { ASSETS_ICON, DATA_ICON, MODS_ICON, PLANNED_ICON, WORKSPACE_ICON } from './icons';

/** Tyrant's tools, in dock order. */
export function registerTools(registry: Registry): void {
  registry.register({
    id: 'home',
    name: 'Home',
    blurb: 'What Tyrant is',
    icon: '',
    status: 'ready',
    instances: 'single',
    closable: false,
    inDock: false,
    load: () => import('$lib/home/HomeTool.svelte'),
  });
  registry.register({
    id: 'workspace',
    name: 'Workspace',
    blurb: 'Game folder, workspace and refreshing its outputs',
    icon: WORKSPACE_ICON,
    status: 'ready',
    instances: 'single',
    load: () => import('$lib/workspace/WorkspaceView.svelte'),
  });
  registry.register({
    id: 'mods',
    name: 'Mods',
    blurb: 'Make, check and install mods',
    icon: MODS_ICON,
    status: 'ready',
    instances: 'single',
    load: () => import('$lib/mods/ModsView.svelte'),
  });
  registry.register({
    id: 'assets',
    name: 'Assets',
    blurb: 'Browse, preview and export game assets',
    icon: ASSETS_ICON,
    status: 'ready',
    instances: 'many',
    load: () => import('$lib/assets/AssetsView.svelte'),
  });
  registry.register({
    id: 'data',
    name: 'Data',
    blurb: 'Browse and compare the dumped game data',
    icon: DATA_ICON,
    status: 'ready',
    instances: 'many',
    load: () => import('$lib/data/DataView.svelte'),
  });
  registry.register({ id: 'models', name: 'Model replacements', blurb: "Swap an animal's model", icon: PLANNED_ICON, status: 'planned', instances: 'single' });
  registry.register({ id: 'scripts', name: 'Script mods', blurb: 'C# scripts compiled by Tyrant', icon: PLANNED_ICON, status: 'planned', instances: 'single' });
}
