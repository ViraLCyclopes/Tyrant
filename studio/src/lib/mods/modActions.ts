import type { Tab } from '$lib/shell/tab.svelte';
import type { Session } from '$lib/stores/session.svelte';

// Mod actions shared by the Mods list and a mod's editor tab (its Mod menu), so both ask and report the same way.

export async function installMod(session: Session, tab: Tab, id: string, name: string): Promise<boolean> {
  const list = await tab.quietly(() => session.rpc.call('mods.list'));
  const message = list?.frameworkInstalled
    ? `Copy '${name}' into the game (UserData\\Tyrant\\Mods\\${id})? No game file is replaced; Remove from game undoes it.`
    : `Install '${name}' into the game? This also installs MelonLoader (if needed) and Tyrant's framework. No game file is replaced; Uninstall from game on the Workspace tab undoes everything.`;
  if (!(await session.platform.confirm(message, 'Install to game'))) return false;
  const r = await session.runJob('mods.install', { id }, `Install ${id}`, tab);
  if (!r) return false;
  tab.info(r.message);
  for (const warning of r.warnings) tab.warn(warning);
  return true;
}

export async function removeFromGame(session: Session, tab: Tab, id: string, name: string): Promise<boolean> {
  if (!(await session.platform.confirm(`Remove '${name}' from the game?`, 'Remove from game'))) return false;
  return (await tab.safely(() => session.rpc.call('mods.remove', { id }))) !== null;
}

export async function restoreCutouts(session: Session, tab: Tab, id: string): Promise<boolean> {
  const r = await tab.safely(() => session.rpc.call('mods.restoreCutouts', { id }));
  if (!r) return false;
  tab.info(
    r.restored.length
      ? `Restored the see-through parts of ${r.restored.length} PNG${r.restored.length === 1 ? '' : 's'} in '${id}'. Install it again to update the game.`
      : `Nothing to restore in '${id}'.`,
  );
  for (const problem of r.problems) tab.warn(problem);
  return true;
}
