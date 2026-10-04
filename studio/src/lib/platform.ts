import { getVersion } from '@tauri-apps/api/app';
import { convertFileSrc, invoke } from '@tauri-apps/api/core';
import { ask, open, save } from '@tauri-apps/plugin-dialog';
import { openUrl, revealItemInDir } from '@tauri-apps/plugin-opener';
import { relaunch } from '@tauri-apps/plugin-process';
import { check, type Update } from '@tauri-apps/plugin-updater';

/** A newer Tyrant on GitHub: its version and release notes. */
export interface AvailableUpdate {
  version: string;
  notes: string;
}

/** Native capabilities the UI needs; faked in tests. */
export interface Platform {
  pickFolder(title: string): Promise<string | null>;
  saveFile(title: string, defaultPath: string, extension: string): Promise<string | null>;
  confirm(message: string, title: string): Promise<boolean>;
  /** Asks for an existing file with one of the extensions; null when cancelled. */
  openFile(title: string, extensions: string[]): Promise<string | null>;
  reveal(path: string): Promise<void>;
  copy(text: string): Promise<void>;
  /** URL the webview can load a local preview file from (Tauri's asset protocol). */
  fileUrl(path: string): string;
  /** Opens a workspace's cache\previews folder to the asset protocol. */
  allowPreviews(dir: string): Promise<void>;
  /** This build's version (tauri.conf.json). */
  appVersion(): Promise<string>;
  /** GitHub's newer release, null when up to date, 'unavailable' when this build has no updater key; throws when the check fails. */
  checkForUpdate(): Promise<AvailableUpdate | null | 'unavailable'>;
  /** Downloads and installs the update checkForUpdate found, then restarts Tyrant; onProgress gets 0..1, or null when the size is unknown. */
  installUpdate(onProgress: (fraction: number | null) => void): Promise<void>;
  /** Opens a web page in the default browser. */
  openUrl(url: string): Promise<void>;
}

/** The update the last check found (installUpdate needs the plugin's object). */
let found: Update | null = null;

export const tauriPlatform: Platform = {
  async pickFolder(title) {
    const picked = await open({ directory: true, multiple: false, title });
    return typeof picked === 'string' ? picked : null;
  },
  async saveFile(title, defaultPath, extension) {
    return (await save({ title, defaultPath, filters: [{ name: extension.toUpperCase(), extensions: [extension] }] })) ?? null;
  },
  confirm: (message, title) => ask(message, { title, kind: 'warning' }),
  async openFile(title, extensions) {
    const picked = await open({ multiple: false, directory: false, title, filters: [{ name: extensions.join(', ').toUpperCase(), extensions }] });
    return typeof picked === 'string' ? picked : null;
  },
  reveal: (path) => revealItemInDir(path),
  copy: (text) => navigator.clipboard.writeText(text),
  fileUrl: (path) => convertFileSrc(path),
  allowPreviews: (dir) => invoke('allow_previews', { dir }),
  appVersion: () => getVersion(),
  async checkForUpdate() {
    if (!(await invoke<boolean>('updater_ready'))) return 'unavailable';
    found = await check();
    return found ? { version: found.version, notes: found.body ?? '' } : null;
  },
  async installUpdate(onProgress) {
    if (!found) throw new Error('Check for updates first.');
    let total = 0;
    let done = 0;
    await found.downloadAndInstall((event) => {
      if (event.event === 'Started') total = event.data.contentLength ?? 0;
      if (event.event === 'Progress') {
        done += event.data.chunkLength;
        onProgress(total ? done / total : null);
      }
    });
    await relaunch();
  },
  openUrl: (url) => openUrl(url),
};
