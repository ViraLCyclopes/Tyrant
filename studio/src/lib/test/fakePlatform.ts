import type { AvailableUpdate, Platform } from '$lib/platform';

/** Answers dialogs from queues and records what the UI asked for. */
export class FakePlatform implements Platform {
  folders: (string | null)[] = [];
  saves: (string | null)[] = [];
  confirmAnswer = true;
  readonly confirms: string[] = [];
  readonly revealed: string[] = [];
  readonly copied: string[] = [];

  files: (string | null)[] = [];
  /** Each openFile call's title and extensions, in order. */
  readonly fileDialogs: { title: string; extensions: string[] }[] = [];

  async openFile(title = '', extensions: string[] = []): Promise<string | null> {
    this.fileDialogs.push({ title, extensions });
    return this.files.shift() ?? null;
  }

  /** Each openFiles call takes the next list (empty: cancelled). */
  fileLists: string[][] = [];

  async openFiles(): Promise<string[]> {
    return this.fileLists.shift() ?? [];
  }

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

  /** Set to make allowPreviews reject. */
  previewsError: Error | null = null;

  async allowPreviews(dir: string): Promise<void> {
    if (this.previewsError) throw this.previewsError;
    this.allowed.push(dir);
  }

  version = '0.1.0';
  /** What checkForUpdate answers; checkError makes it throw. */
  update: AvailableUpdate | null | 'unavailable' = null;
  checkError: Error | null = null;
  checks = 0;
  installed = 0;
  readonly opened: string[] = [];

  async appVersion(): Promise<string> {
    return this.version;
  }

  async checkForUpdate(): Promise<AvailableUpdate | null | 'unavailable'> {
    this.checks++;
    if (this.checkError) throw this.checkError;
    return this.update;
  }

  /** installUpdate waits for this (a download in progress) and reports these fractions first. */
  installGate: Promise<void> | null = null;
  progressReports: (number | null)[] = [1];

  async installUpdate(onProgress: (fraction: number | null) => void): Promise<void> {
    this.installed++;
    for (const fraction of this.progressReports) onProgress(fraction);
    if (this.installGate) await this.installGate;
  }

  async openUrl(url: string): Promise<void> {
    this.opened.push(url);
  }
}
