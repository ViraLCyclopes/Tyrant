import { convertFileSrc, invoke } from '@tauri-apps/api/core';
import { ask, open, save } from '@tauri-apps/plugin-dialog';
import { revealItemInDir } from '@tauri-apps/plugin-opener';

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
}

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
};
