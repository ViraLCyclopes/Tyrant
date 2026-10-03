import { ask, open, save } from '@tauri-apps/plugin-dialog';
import { revealItemInDir } from '@tauri-apps/plugin-opener';

/** Native capabilities the UI needs; faked in tests. */
export interface Platform {
  pickFolder(title: string): Promise<string | null>;
  saveFile(title: string, defaultPath: string, extension: string): Promise<string | null>;
  confirm(message: string, title: string): Promise<boolean>;
  reveal(path: string): Promise<void>;
  copy(text: string): Promise<void>;
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
  reveal: (path) => revealItemInDir(path),
  copy: (text) => navigator.clipboard.writeText(text),
};
