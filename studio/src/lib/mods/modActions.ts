import { asRpcError } from '$lib/rpc/client';
import type { ModImportResult, ModRow, ModsListResult } from '$lib/rpc/types.gen';
import type { Tab } from '$lib/shell/tab.svelte';
import type { LooksPromptMod, Session } from '$lib/stores/session.svelte';

// Mod actions shared by the Mods list and a mod's editor tab (its Mod menu), so both ask and report the same way.

/** The install action's words for where the mod stands in the game: not there, edited since, or up to date. */
export function installLabel(state: string | null | undefined): string {
  return state === 'changed' ? 'Update in game' : state === 'installed' ? 'Refresh in game' : 'Install to game';
}

/** The same action for a screen reader, naming the mod: "Install Red spot to the game", "Update Red spot in the game". */
export function installAria(state: string | null | undefined, name: string): string {
  return state === 'changed' ? `Update ${name} in the game` : state === 'installed' ? `Refresh ${name} in the game` : `Install ${name} to the game`;
}

/**
 * Before install, remove, delete-with-remove and uninstall: the in-game editor may have saved looks into these mods'
 * installed copies (ModRow.gameLooks), which the action would throw away. Asks which to keep (BringInLooksDialog), brings
 * those material folders into the workspace and copies the chosen mods that are only in the game there. False when the
 * user cancelled or a step failed: the action must not go on.
 */
export async function keepGameLooks(session: Session, tab: Tab, rows: ModRow[], action: string): Promise<boolean> {
  const asked: LooksPromptMod[] = [];
  for (const row of rows.filter((r) => r.gameLooks)) {
    if (row.state === 'gameOnly') {
      asked.push({ id: row.id, name: row.name, changes: [], gameOnly: true });
      continue;
    }
    if (row.state === 'notInstalled') continue;
    const r = await tab.safely(() => session.rpc.call('mods.looksChanges', { id: row.id }));
    if (!r) return false;
    const changes = r.changes.filter((c) => c.gameChanged);
    if (changes.length) asked.push({ id: row.id, name: row.name, changes, gameOnly: false });
  }
  if (!asked.length) return true;
  const answer = await session.askLooks(action, asked);
  if (!answer) return false;
  for (const mod of asked) {
    const folders = answer.bringIn[mod.id] ?? [];
    if (!folders.length) continue;
    if ((await tab.safely(() => session.rpc.call('mods.bringInLooks', { id: mod.id, folders }))) === null) return false;
    tab.info(`Brought ${folders.length} ${folders.length === 1 ? 'look' : 'looks'} made in the game into '${mod.name}' in the workspace.`);
  }
  for (const id of answer.copy) if (!(await copyFromGame(session, tab, id))) return false;
  return true;
}

/** Bring in looks… (Mods list): the looks made in the game, into the workspace, without installing or removing anything. */
export async function bringInLooks(session: Session, tab: Tab, row: ModRow): Promise<boolean> {
  return keepGameLooks(session, tab, [row], '');
}

/** The mod's row in the Mods list, or null when the list cannot be read (the action then goes on without asking about looks). */
async function rowOf(session: Session, id: string): Promise<ModRow | null> {
  try {
    return (await session.rpc.call('mods.list')).mods.find((m) => m.id === id) ?? null;
  } catch {
    return null;
  }
}

export async function installMod(session: Session, tab: Tab, id: string, name: string): Promise<boolean> {
  const list = await tab.quietly(() => session.rpc.call('mods.list'));
  const row = list?.mods.find((m) => m.id === id);
  const state = row?.state;
  if (row && !(await keepGameLooks(session, tab, [row], `Installing '${name}' again`))) return false;
  const where = `UserData\\Tyrant\\Mods\\${id}`;
  const message = !list?.frameworkInstalled
    ? `Install '${name}' into the game? This also installs MelonLoader (if needed) and Tyrant's framework. No game file is replaced; Uninstall from game on the Workspace tab undoes everything.`
    : state === 'changed'
      ? `Copy your changes to '${name}' into the game (${where})? No game file is replaced; Remove from game undoes it.`
      : state === 'installed'
        ? `'${name}' is already in the game and up to date. Copy it again (${where})?`
        : `Copy '${name}' into the game (${where})? No game file is replaced; Remove from game undoes it.`;
  if (!(await session.platform.confirm(message, installLabel(state)))) return false;
  const r = await session.runJob('mods.install', { id }, `Install ${id}`, tab);
  if (!r) return false;
  tab.info(r.message);
  for (const warning of r.warnings) tab.warn(warning);
  return true;
}

export async function removeFromGame(session: Session, tab: Tab, id: string, name: string): Promise<boolean> {
  const row = await rowOf(session, id);
  if (row && !(await keepGameLooks(session, tab, [row], `Removing '${name}' from the game`))) return false;
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
export async function exportMod(session: Session, tab: Tab, id: string, name: string, version: string, includePngs = false): Promise<boolean> {
  const out = await session.platform.saveFile(`Export '${name}' for sharing`, `${id}-${version}.zip`, 'zip');
  if (!out) return false;
  const r = await session.runJob('mods.export', { id, out, includePngs }, `Export ${id}`, tab);
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
  tab.info(`Added '${result.id}' to the workspace.${extractedNote(result)} Open it to check it, then Install to game.`);
  for (const problem of result.problems) tab.warn(problem);
  return result.mods;
}

/** " 3 textures were extracted from its cooked pack; …" for a mod brought in without its PNGs; empty otherwise. */
function extractedNote(result: ModImportResult): string {
  return result.extracted
    ? ` ${result.extracted} textures were extracted from its cooked pack; they are slightly lower quality than the author's originals.`
    : '';
}

/** Copy to workspace (Mods tab, a mod only in the game): asks before replacing a workspace mod with the same id; returns the refreshed list. */
export async function copyFromGame(session: Session, tab: Tab, id: string): Promise<ModsListResult | null> {
  let result: ModImportResult | null;
  try {
    result = await session.rpc.call('mods.copyFromGame', { id, replace: false });
  } catch (e) {
    const error = asRpcError(e);
    if (error.code !== 'MOD_EXISTS') {
      tab.fail(error);
      return null;
    }
    const question = `${error.message.split('. ')[0]}. Replace it with the one in the game?`;
    if (!(await session.platform.confirm(question, 'Copy to workspace'))) return null;
    result = await tab.safely(() => session.rpc.call('mods.copyFromGame', { id, replace: true }));
  }
  if (!result) return null;
  tab.info(`Copied '${result.id}' into the workspace.${extractedNote(result)} Open it to edit it.`);
  for (const problem of result.problems) tab.warn(problem);
  return result.mods;
}

/** Extract textures… (Mods tab): any textures.tpack to PNGs in a folder the user picks. */
export async function extractTextures(session: Session, tab: Tab): Promise<boolean> {
  const pack = await session.platform.openFile('Choose a texture pack to extract', ['tpack']);
  if (!pack) return false;
  const out = await session.platform.pickFolder('Choose a folder for the extracted PNGs');
  if (!out) return false;
  const r = await session.runJob('textures.extract', { pack, out }, 'Extract textures', tab);
  if (!r) return false;
  tab.info(`Extracted ${r.written} textures into ${r.out}.`);
  if (r.skipped.length) tab.warn(`${r.skipped.length} could not be extracted: ${r.skipped.join(', ')}`);
  return true;
}

/** Delete mod (Mods list, and the mod's Mod menu): to the Recycle Bin, closing its editor tab. A failure is for the dialog to show. */
export async function deleteMod(
  session: Session,
  tab: Tab,
  id: string,
  name: string,
  removeFromGame: boolean,
): Promise<{ list: ModsListResult } | { error: string }> {
  const row = removeFromGame ? await rowOf(session, id) : null;
  if (row && !(await keepGameLooks(session, tab, [row], `Deleting '${name}' with its copy in the game`)))
    return { error: 'Nothing was deleted: the looks made in the game were not brought in.' };
  try {
    const list = await session.rpc.call('mods.delete', { id, removeFromGame });
    // Close first: a closed tab's log goes with it, and the closed tab then logs to every tab, so the message stays.
    tab.closeTool('mod', id);
    tab.info(`'${name}' went to the Recycle Bin${removeFromGame ? ' and is out of the game' : ''}. Its skin numbers stay reserved: Clean up skin numbers… frees them.`);
    return { list };
  } catch (e) {
    const error = asRpcError(e);
    tab.fail(error);
    return { error: error.message };
  }
}
