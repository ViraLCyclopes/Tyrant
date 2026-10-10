import type { ModTexturesResult } from '$lib/rpc/types.gen';
import type { Session } from '$lib/stores/session.svelte';
import type { ModDoc } from './modDoc.svelte';

/** The mod's cooked-texture estimates, re-read after every save, undo or file change; an answer for an older state is dropped. */
export function textureFacts(session: Session, doc: ModDoc): { readonly current: ModTexturesResult | null } {
  let current = $state<ModTexturesResult | null>(null);
  $effect(() => {
    const key = `${doc.detail?.revision}|${doc.detail?.filesStamp}`;
    if (!doc.detail) return;
    let stale = false;
    session.rpc.call('mods.textures', { id: doc.id }).then(
      (r) => { if (!stale) current = r; },
      () => { if (!stale) current = null; },
    );
    void key;
    return () => { stale = true; };
  });
  return { get current() { return current; } };
}
