import type { ModTexturesResult } from '$lib/rpc/types.gen';
import type { Session } from '$lib/stores/session.svelte';
import type { ModDoc } from './modDoc.svelte';

/**
 * The mod's cooked-texture estimates, shared by every page of one open mod: one mods.textures call per revision and
 * files stamp (it is not cheap), re-read after every save, undo or file change. An answer for an older state is dropped.
 */
export class TextureFacts {
  current = $state.raw<ModTexturesResult | null>(null);
  #key: string | null = null;
  #generation = 0;

  constructor(
    private readonly session: Session,
    private readonly doc: ModDoc,
  ) {}

  /** Reads the doc's state, so call it inside an $effect; fetches only when that state is new. */
  load(): void {
    const detail = this.doc.detail;
    if (!detail) return;
    const key = `${detail.revision}|${detail.filesStamp}`;
    if (key === this.#key) return;
    this.#key = key;
    const generation = ++this.#generation;
    this.session.rpc.call('mods.textures', { id: this.doc.id }).then(
      (r) => { if (generation === this.#generation) this.current = r; },
      () => { if (generation === this.#generation) this.current = null; },
    );
  }
}

const shared = new WeakMap<ModDoc, TextureFacts>();

/** The one TextureFacts of a doc; pages call `$effect(() => facts.load())` so the first one starts the fetch. */
export function textureFacts(session: Session, doc: ModDoc): TextureFacts {
  let facts = shared.get(doc);
  if (!facts) shared.set(doc, (facts = new TextureFacts(session, doc)));
  return facts;
}
