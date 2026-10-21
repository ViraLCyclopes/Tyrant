<script lang="ts">
  import type { ModSizesDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * One species' size changes: the adult size, the newborn's share of it, and each sex's multiplier. A key the mod leaves
   * unset shows the game's value and Reset puts it back. The sex sizes multiply each skin's own, so theirs is 1× (the skin's
   * own shows as information only). The server replaces the whole set, so every save carries all four.
   */
  let { doc, target }: { doc: ModDoc; target: string } = $props();
  const session = getSession();
  const tab = getTab();

  type Key = 'adult' | 'baby' | 'male' | 'female';
  const FIELDS: { key: Key; label: string; min: number; max: number; step: number }[] = [
    { key: 'adult', label: 'Adult size ×', min: 0.05, max: 5, step: 0.01 },
    { key: 'baby', label: 'Baby size (share of adult)', min: 0.01, max: 1, step: 0.01 },
    { key: 'male', label: 'Male size ×', min: 0.1, max: 5, step: 0.01 },
    { key: 'female', label: 'Female size ×', min: 0.1, max: 5, step: 0.01 },
  ];

  let loaded = $state.raw<ModSizesDto | null>(null);
  let failed = $state(false);
  let own = $state<Record<Key, number | null>>({ adult: null, baby: null, male: null, female: null });
  let problem = $state('');
  let loads = 0; // the newest request: older replies (another target, or out of order) are dropped

  // Follows the target, saves, undo and outside changes.
  $effect(() => {
    void doc.detail?.revision;
    const t = target;
    const mine = ++loads;
    void tab.quietly(() => session.rpc.call('mods.sizes', { id: doc.id, target: t })).then((r) => {
      if (mine !== loads) return;
      failed = r === null;
      if (!r) return;
      loaded = r;
      own = { adult: r.adult, baby: r.baby, male: r.male, female: r.female };
      problem = '';
    });
  });
  const ready = $derived(loaded?.target.toLowerCase() === target.toLowerCase() ? loaded : null);

  // Unchanged values: adult, male and female are multipliers (1×); baby is the game's own share.
  const original = $derived<Record<Key, number>>({ adult: 1, baby: ready?.original.babyShare ?? 1, male: 1, female: 1 });
  const round = (n: number) => Number(n.toFixed(3));
  const skin = $derived(ready?.original.skins[0]);
  const sexHint = $derived(skin ? `game: ${round(skin.male)} male / ${round(skin.female)} female` : '');
  const shown = (key: Key) => own[key] ?? original[key];

  let saves = 0;
  async function save(next: Record<Key, number | null>) {
    own = next;
    problem = '';
    const [mine, reads, saved] = [++saves, loads, ready];
    // Not saved (the edit failed, nothing changed on disk): back to what the mod has, unless a later save or a reload came first.
    if (!(await doc.edit('mods.setSizes', { target, ...next })) && mine === saves && reads === loads && saved)
      own = { adult: saved.adult, baby: saved.baby, male: saved.male, female: saved.female };
  }

  function change(f: (typeof FIELDS)[number], text: string) {
    const n = Number(text);
    if (text.trim() === '' || !Number.isFinite(n) || n < f.min || n > f.max) {
      problem = `${f.label}: use a number from ${f.min} to ${f.max}.`;
      return;
    }
    void save({ ...own, [f.key]: n });
  }

  // Adult length before and after, and the newborn's (a share of the adult), when the game's model length is known.
  const length = $derived(ready?.original.adultLength ?? null);
  const finalLength = $derived(ready?.finalAdultLength ?? null);
  const metres = (n: number) => `${n.toFixed(1)} m`;
  const adultText = $derived(length === null ? `× ${original.adult} → × ${shown('adult')}` : `${metres(length)} → ${metres(finalLength ?? length)}`);
  const babyText = $derived(
    length === null
      ? `${(original.baby * 100).toFixed(0)}% → ${(shown('baby') * 100).toFixed(0)}% of adult`
      : `${metres(length * original.baby)} → ${metres((finalLength ?? length) * shown('baby'))}`,
  );
  const widest = $derived(Math.max(length ?? 1, finalLength ?? length ?? 1, 1e-9));
  const barFor = (adult: number, baby: number) => ({ adult: Math.min(100, (adult / (length === null ? Math.max(1, shown('adult')) : widest)) * 100), baby: baby * 100 });

  // Check's messages about this species' file (sizes/<file>: …), matched exactly: one species' name can contain another's.
  const issues = $derived(
    ready ? [...(doc.check?.errors ?? []), ...(doc.check?.warnings ?? [])].filter((m) => m.startsWith(`sizes/${ready.file}:`)) : [],
  );
</script>

<fieldset class="card sizes" aria-label="Size">
  <legend>Size</legend>
  {#if !ready}
    <p class="hint">{failed ? 'The size could not be read; the log says why.' : 'Reading the size…'}</p>
  {:else}
    {#each FIELDS as f (f.key)}
      <div class="field" class:changed={own[f.key] !== null}>
        <span class="name">{f.label}</span>
        <input type="number" aria-label={f.label} min={f.min} max={f.max} step={f.step} value={shown(f.key)} onchange={(e) => change(f, e.currentTarget.value)} />
        <input type="range" aria-label="{f.label} slider" min={f.min} max={f.max} step={f.step} value={shown(f.key)} onchange={(e) => change(f, e.currentTarget.value)} />
        <span class="hint">{f.key === 'male' || f.key === 'female' ? '' : `game: ${round(original[f.key])}`}</span>
        {#if own[f.key] !== null}
          <button class="ghost" aria-label="Reset {f.label}" title={f.key === 'male' || f.key === 'female' ? "Back to 1× (the skin's own size)" : "Back to the game's value"} onclick={() => save({ ...own, [f.key]: null })}>↺</button>
        {/if}
      </div>
    {/each}
    {#if sexHint}<p class="hint" title="Each skin's own sex sizes; Male and Female multiply them">{sexHint}</p>{/if}
    <div class="bars" aria-label="Size bar">
      <div class="bar"><span class="fill game" style:width="{barFor(length ?? original.adult, original.baby).adult}%"><span class="baby" style:width="{original.baby * 100}%"></span></span></div>
      <div class="bar"><span class="fill mod" style:width="{barFor(finalLength ?? shown('adult'), shown('baby')).adult}%"><span class="baby" style:width="{shown('baby') * 100}%"></span></span></div>
      <p class="hint">Adult <span>{adultText}</span></p>
      <p class="hint">Baby <span>{babyText}</span></p>
    </div>
    {#if ready.recordEdits.length > 0}
      <!-- Edits made in the species' record before Size existed: the field editor hides them, so they are cleared here. -->
      <p class="warn">
        Old record edits change this species' size outside Size: {ready.recordEdits.join(', ')}.
        <button type="button" onclick={() => doc.edit('mods.removeSizeEdits', { target }, { undoable: false })}>Remove old size edits</button>
      </p>
    {/if}
    {#if problem}<p class="warn" role="alert">{problem}</p>{/if}
    {#each issues as m (m)}<p class="warn">{m}</p>{/each}
  {/if}
</fieldset>

<style>
  .sizes { display: grid; gap: 6px; }
  .field { display: grid; grid-template-columns: minmax(170px, 1fr) 90px minmax(100px, 160px) auto auto; gap: 8px; align-items: center; }
  .field.changed .name { font-weight: 600; }
  .bars { display: grid; gap: 4px; }
  .bars p { margin: 0; }
  .bar { height: 10px; background: var(--border); border-radius: 5px; overflow: hidden; }
  .fill { display: block; height: 100%; }
  .fill.game { background: var(--muted); }
  .fill.mod { background: var(--accent, #4a9); }
  .baby { display: block; height: 100%; background: rgba(255, 255, 255, 0.35); }
</style>
