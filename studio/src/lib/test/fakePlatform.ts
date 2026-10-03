import type { Platform } from '$lib/platform';

/** Answers dialogs from queues and records what the UI asked for. */
export class FakePlatform implements Platform {
  folders: (string | null)[] = [];
  saves: (string | null)[] = [];
  confirmAnswer = true;
  readonly confirms: string[] = [];
  readonly revealed: string[] = [];
  readonly copied: string[] = [];

  async pickFolder(): Promise<string | null> {
    return this.folders.shift() ?? null;
  }

  async saveFile(): Promise<string | null> {
    return this.saves.shift() ?? null;
  }

  async confirm(message: string): Promise<boolean> {
    this.confirms.push(message);
    return this.confirmAnswer;
  }

  async reveal(path: string): Promise<void> {
    this.revealed.push(path);
  }

  async copy(text: string): Promise<void> {
    this.copied.push(text);
  }

  readonly allowed: string[] = [];

  fileUrl(path: string): string {
    return `asset://${path}`;
  }

  async allowPreviews(dir: string): Promise<void> {
    this.allowed.push(dir);
  }
}
