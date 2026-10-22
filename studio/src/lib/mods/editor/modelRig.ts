import { writable } from 'svelte/store';
import type { Rpc } from '$lib/rpc/client';
import type { ModModelRigPreviewDto, ModReadModelRigParams } from '$lib/rpc/types.gen';

export type RigChoice = 'auto' | 'useFile' | 'keepCurrent';

/** The rig-edit question waiting for an answer; ModelRigHost shows it inside the card that asked. */
export interface PendingRig {
  preview: ModModelRigPreviewDto;
  action: 'Add' | 'Replace';
  resolve: (choice: RigChoice | null) => void;
}

export const pendingRig = writable<PendingRig | null>(null);

/**
 * Reads the rig edit in the picked model and, when it is worth a look, asks first. Resolves with what to send as `rig`, or null
 * when the user cancelled (then nothing is added).
 */
export async function chooseModelRig(
  rpc: Pick<Rpc, 'call'>,
  params: ModReadModelRigParams,
  options: { reimport: boolean; action?: 'Add' | 'Replace' },
): Promise<RigChoice | null> {
  const preview = await rpc.call('mods.readModelRig', params);
  const clean = preview.errors.length === 0;
  if (options.reimport ? !preview.changes && clean : preview.question === 'none' && preview.bones.length === 0 && clean && preview.warnings.length === 0) return 'auto';
  return new Promise((resolve) => {
    const done = (choice: RigChoice | null) => {
      pendingRig.set(null);
      resolve(choice);
    };
    pendingRig.update((old) => {
      old?.resolve(null);
      return { preview, action: options.action ?? (options.reimport ? 'Replace' : 'Add'), resolve: done };
    });
  });
}
