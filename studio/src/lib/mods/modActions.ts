import { asRpcError } from '$lib/rpc/client';
import type { ModImportResult, ModsListResult } from '$lib/rpc/types.gen';
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

/** Export for sharing: a zip players unzip into the game folder (mod editor's Mod menu). */
export async function exportMod(session: Session, tab: Tab, id: string, name: string, version: string): Promise<boolean> {
  const out = await session.platform.saveFile(`Export '${name}' for sharing`, `${id}-${version}.zip`, 'zip');
  if (!out) return false;
  const r = await session.runJob('mods.export', { id, out }, `Export ${id}`, tab);
  if (!r) return false;
  tab.info(`Exported '${name}' to ${r.path}. Players unzip it into the game folder (it needs MelonLoader and Tyrant's framework; the zip's README says how).`);
  for (const warning of r.warnings) tab.warn(warning);
  return true;
}

/** Add mod from zip (Mods tab): asks before replacing a mod with the same id; returns the refreshed list. */
export async function importModZip(session: Session, tab: Tab): Promise<ModsListResult | null> {
  const file = await session.platform.openFile('Add a mod from a zip', ['zip']);
  if (!file) return null;
  let result: ModImportResult | null;
  try {
    result = await session.rpc.call('mods.import', { file, replace: false });
  } catch (e) {
    const error = asRpcError(e);
    if (error.code !== 'MOD_EXISTS') {
      tab.fail(error);
      return null;
    }
    const question = `${error.message.split('. ')[0]}. Replace it with the one in the zip?`;
    if (!(await session.platform.confirm(question, 'Add mod from zip'))) return null;
    result = await tab.safely(() => session.rpc.call('mods.import', { file, replace: true }));
  }
  if (!result) return null;
  tab.info(`Added '${result.id}' to the workspace. Open it to check it, then Install to game.`);
  for (const problem of result.problems) tab.warn(problem);
  return result.mods;
}
