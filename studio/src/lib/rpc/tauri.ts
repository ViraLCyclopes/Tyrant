import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import type { ExitInfo, Transport } from './client';

/** True inside the Tauri app; false when the UI is opened in a plain browser (`npm run dev`). */
export function insideTauri(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

/** Talks to the tyrant sidecar through the Rust shell: lines arrive as events, requests go out through `core_send`. */
export class TauriTransport implements Transport {
  private readonly lineHandlers: ((line: string) => void)[] = [];
  private readonly exitHandlers: ((info: ExitInfo) => void)[] = [];
  private readonly startedHandlers: (() => void)[] = [];
  private readonly ready: Promise<unknown>;

  constructor() {
    this.ready = Promise.all([
      listen<string>('tyrant://line', (event) => this.lineHandlers.forEach((h) => h(event.payload))),
      listen<ExitInfo>('tyrant://exit', (event) => this.exitHandlers.forEach((h) => h(event.payload))),
      listen('tyrant://started', () => this.startedHandlers.forEach((h) => h())),
    ]);
  }

  async send(line: string): Promise<void> {
    await this.ready; // listeners first, so no response can arrive unheard
    await invoke('core_send', { line });
  }

  onLine(handler: (line: string) => void): void {
    this.lineHandlers.push(handler);
  }

  onExit(handler: (info: ExitInfo) => void): void {
    this.exitHandlers.push(handler);
  }

  onStarted(handler: () => void): void {
    this.startedHandlers.push(handler);
  }
}
