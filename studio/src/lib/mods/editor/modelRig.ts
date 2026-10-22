import { writable, type Readable } from 'svelte/store';
import type { Rpc } from '$lib/rpc/client';
import type { ModModelRigPreviewDto, ModReadModelRigParams } from '$lib/rpc/types.gen';

export type RigChoice = 'auto' | 'useFile' | 'keepCurrent';

/** The rig-edit question waiting for an answer in one card; that card's ModelRigHost shows it. */
export interface PendingRig {
  preview: ModModelRigPreviewDto;
  action: 'Add' | 'Replace';
  resolve: (choice: RigChoice | null) => void;
}

/** One card's place for the question: only the card that asked shows it, and it is answered null when the card goes away. */
export interface RigAsk {
  readonly pending: Readable<PendingRig | null>;
  ask(preview: ModModelRigPreviewDto, action: 'Add' | 'Replace'): Promise<RigChoice | null>;
  dispose(): void;
}

export function createRigAsk(): RigAsk {
  const store = writable<PendingRig | null>(null);
  let answer: ((choice: RigChoice | null) => void) | null = null;
  const settle = (choice: RigChoice | null) => {
    const done = answer;
    answer = null;
    store.set(null);
    done?.(choice);
  };
  return {
    pending: { subscribe: store.subscribe },
    ask(preview, action) {
      settle(null);
      return new Promise((resolve) => {
        answer = resolve;
        store.set({ preview, action, resolve: settle });
      });
    },
    dispose: () => settle(null),
  };
}

/**
 * The card's `ask`, but only while `current()` holds (the card still shows the destination the question is about): once the user
 * has moved on, the question is answered null without being shown, so nothing is saved.
 */
export function askFor(ask: RigAsk, current: () => boolean): RigAsk {
  return {
    pending: ask.pending,
    ask: (preview, action) => (current() ? ask.ask(preview, action) : Promise.resolve(null)),
    dispose: () => ask.dispose(),
  };
}

/**
 * Reads the rig edit in the picked model and, when it is worth a look, asks first in the calling card (`ask`). Resolves with what to
 * send as `rig`, or null when the user cancelled or left the card (then nothing is added).
 */
export async function chooseModelRig(
  rpc: Pick<Rpc, 'call'>,
  params: ModReadModelRigParams,
  options: { reimport: boolean; action?: 'Add' | 'Replace' },
  ask?: RigAsk,
): Promise<RigChoice | null> {
  const preview = await rpc.call('mods.readModelRig', params);
  const clean = preview.errors.length === 0;
  if (options.reimport ? !preview.changes && clean : preview.question === 'none' && preview.bones.length === 0 && clean && preview.warnings.length === 0) return 'auto';
  return ask ? ask.ask(preview, options.action ?? (options.reimport ? 'Replace' : 'Add')) : null;
}
