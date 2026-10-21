<script lang="ts">
  import type { ModSizesDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * One species' size changes: the adult size, the newborn's share of it, each sex's multiplier, and the species' genetic size
   * ranges (the Nursery slider's and breeding's) and size limit. A key the mod leaves unset shows the game's value and Reset
   * puts it back. The sex sizes multiply each skin's own, so theirs is 1× (the skin's own shows as information only). The
   * server replaces the whole set, so every save carries all seven. A slider moves its number field, the size bar and the
   * lengths while it is dragged and saves once, on release (one undo step); a save past the species' size limit raises the
   * limit, and the card says so. The number fields show 3 decimals; an unedited value keeps its full precision.
   */
  let { doc, target }: { doc: ModDoc; target: string } = $props();
  const session = getSession();
  const tab = getTab();

  type Key = 'adult' | 'baby' | 'male' | 'female';
  type Field = { key: Key; label: string; min: number; max: number; slider: number; step: number };
  type RangeKey = 'nursery' | 'breeding' | 'limit';
  type Own = Record<Key, number | null> & Record<RangeKey, number[] | null>;
  /** A [min, max] row: each end's bounds (null: the other end's value) and step. */
  type RangeRow = { key: RangeKey; label: string; min: [number, number | null]; max: [number | null, number]; step: [number, number] };
  // The genetic ranges take 0 < min <= max <= 20; the limit's minimum 0.01–0.05 and its maximum 2–20 (as sizes/<file>.json takes them).
  const RANGES: RangeRow[] = [
    { key: 'nursery', label: 'Nursery size slider range', min: [0.01, null], max: [null, 20], step: [0.01, 0.01] },
    { key: 'breeding', label: 'Breeding size range', min: [0.01, null], max: [null, 20], step: [0.01, 0.01] },
    { key: 'limit', label: 'Size limit', min: [0.01, 0.05], max: [2, 20], step: [0.01, 0.5] },
  ];
  // The number fields take what the files take (up to 20×; Check says whether it fits the size limit). The sliders reach 5×,
  // or the species' size limit when it is higher, so their usual range stays easy to drag.
  const MAX = 20;
  const reach = (limit: number[] | undefined) => Math.min(MAX, Math.max(5, limit?.[1] ?? 0));
  const fieldsFor = (top: number): Field[] => [
    { key: 'adult', label: 'Adult size ×', min: 0.05, max: MAX, slider: top, step: 0.01 },
    { key: 'baby', label: 'Baby size (share of adult)', min: 0.01, max: 1, slider: 1, step: 0.01 },
    { key: 'male', label: 'Male size ×', min: 0.1, max: MAX, slider: top, step: 0.01 },
    { key: 'female', label: 'Female size ×', min: 0.1, max: MAX, slider: top, step: 0.01 },
  ];

  let loaded = $state.raw<ModSizesDto | null>(null);
  let failed = $state(false);
  let own = $state<Own>({ adult: null, baby: null, male: null, female: null, nursery: null, breeding: null, limit: null });
  let problem = $state('');
  /** The slider being dragged and its value, shown everywhere until it is let go (and saved). */
  let dragging = $state<{ key: Key; value: number } | null>(null);
  /** What the last save did besides (the size limit it raised), for its target. */
  let noted = $state.raw<{ target: string; notes: string[] }>({ target: '', notes: [] });
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
      own = ownOf(r);
      problem = '';
    });
  });
  const ownOf = (r: ModSizesDto): Own => ({
    adult: r.adult, baby: r.baby, male: r.male, female: r.female, nursery: r.nursery, breeding: r.breeding, limit: r.limit,
  });
  const ready = $derived(loaded?.target.toLowerCase() === target.toLowerCase() ? loaded : null);
  const fields = $derived(fieldsFor(reach(own.limit ?? ready?.gameLimit)));

  // Unchanged values: adult, male and female are multipliers (1×); baby is the game's own share.
  const original = $derived<Record<Key, number>>({ adult: 1, baby: ready?.original.babyShare ?? 1, male: 1, female: 1 });
  const round = (n: number) => Number(n.toFixed(3));
  const skin = $derived(ready?.original.skins[0]);
  const sexHint = $derived(skin ? `game: ${round(skin.male)} male / ${round(skin.female)} female` : '');
  const shown = (key: Key) => (dragging?.key === key ? dragging.value : (own[key] ?? original[key]));

  let saves = 0;
  async function save(next: Own) {
    own = next;
    problem = '';
    noted = { target: '', notes: [] };
    const [mine, reads, saved, t] = [++saves, loads, ready, target];
    const ok = await doc.edit('mods.setSizes', { target: t, ...next });
    // Saved: what the save did besides (the server raised the size limit to fit) shows under the fields.
    if (ok && mine === saves) noted = { target: t, notes: doc.detail?.notes ?? [] };
    // Not saved (the edit failed, nothing changed on disk): back to what the mod has, unless a later save or a reload came first.
    if (!ok && mine === saves && reads === loads && saved) own = ownOf(saved);
  }

  // A range unset shows the game's (the base species', for a custom species its template's), and an edit starts from it.
  const gameOf = (key: RangeKey) => (key === 'nursery' ? ready?.gameNursery : key === 'breeding' ? ready?.gameBreeding : ready?.gameLimit) ?? [0, 0];
  const rangeShown = (key: RangeKey) => own[key] ?? gameOf(key);
  const pairText = (v: number[]) => `${round(v[0])} – ${round(v[1])}`;

  /** One end of a range from its number field; a number outside that end's bounds is not saved. */
  function changeEnd(row: RangeRow, end: 0 | 1, text: string) {
    const now = rangeShown(row.key);
    const [lo, hi] = end === 0 ? [row.min[0], row.min[1] ?? now[1]] : [row.max[0] ?? now[0], row.max[1]];
    const n = Number(text);
    // A genetic range's minimum only has to be above 0 (its field's 0.01 is a step, not a bound); the limit's ends have bounds.
    const low = row.key !== 'limit' && end === 0 ? n <= 0 : n < lo;
    if (text.trim() === '' || !Number.isFinite(n) || low || n > hi) {
      const which = end === 0 ? 'minimum' : 'maximum';
      problem =
        row.key === 'limit'
          ? `${row.label}: the ${which} must be from ${lo} to ${hi}.`
          : end === 0
            ? `${row.label}: the minimum must be above 0 and at most the maximum (${round(now[1])}).`
            : `${row.label}: the maximum must be from the minimum (${round(now[0])}) to ${hi}.`;
      return;
    }
    void save({ ...own, [row.key]: end === 0 ? [n, now[1]] : [now[0], n] });
  }

  /** A slider being dragged: everything follows it, nothing is saved until it is let go. */
  function drag(f: Field, text: string) {
    const n = Number(text);
    if (Number.isFinite(n)) dragging = { key: f.key, value: n };
  }

  /** The slider let go: its value is saved once. */
  function release(f: Field, text: string) {
    change(f, text);
    dragging = null;
  }

  function change(f: Field, text: string) {
    const n = Number(text);
    if (text.trim() === '' || !Number.isFinite(n) || n < f.min || n > f.max) {
      problem = `${f.label}: use a number from ${f.min} to ${f.max}.`;
      return;
    }
    void save({ ...own, [f.key]: n });
  }

  // Adult length before and after, and the newborn's (a share of the adult), when the game's model length is known.
  const length = $derived(ready?.original.adultLength ?? null);
  const finalLength = $derived(length === null ? null : length * shown('adult')); // follows a dragged slider
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
    {#each fields as f (f.key)}
      <div class="field" class:changed={own[f.key] !== null}>
        <span class="name">{f.label}</span>
        <input type="number" aria-label={f.label} min={f.min} max={f.max} step={f.step} value={round(shown(f.key))} onchange={(e) => change(f, e.currentTarget.value)} />
        <input type="range" aria-label="{f.label} slider" min={f.min} max={f.slider} step={f.step} value={shown(f.key)}
          oninput={(e) => drag(f, e.currentTarget.value)} onchange={(e) => release(f, e.currentTarget.value)} />
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
    {#each RANGES as row (row.key)}
      {@const shownRange = rangeShown(row.key)}
      <div class="range" class:changed={own[row.key] !== null} role="group" aria-label={row.label}>
        <span class="name">{row.label}</span>
        <label class="end">Minimum <input type="number" aria-label="{row.label} minimum" min={row.min[0]} max={row.min[1] ?? shownRange[1]} step={row.step[0]}
          value={round(shownRange[0])} onchange={(e) => changeEnd(row, 0, e.currentTarget.value)} /></label>
        <label class="end">Maximum <input type="number" aria-label="{row.label} maximum" min={row.max[0] ?? shownRange[0]} max={row.max[1]} step={row.step[1]}
          value={round(shownRange[1])} onchange={(e) => changeEnd(row, 1, e.currentTarget.value)} /></label>
        <span class="hint">{own[row.key] === null ? "game's" : `game: ${pairText(gameOf(row.key))}`}</span>
        {#if own[row.key] !== null}
          <button class="ghost" aria-label="Reset {row.label}" title="Back to the game's" onclick={() => save({ ...own, [row.key]: null })}>↺</button>
        {/if}
      </div>
      {#if row.key === 'nursery'}
        <p class="hint">The game's own Customize size slider in the Nursery covers this range for this species. Random eggs keep the game's range.</p>
      {:else if row.key === 'limit'}
        <p class="hint">The game caps animals at 0.05×–2×. A wider limit lets sizes past 2× show in game; the game was tuned for normal sizes, so very big animals may clip, get stuck or frame badly.</p>
        <p class="hint">Minimum 0.01–0.05, maximum 2–20. With several mods on, the widest limit wins.</p>
      {/if}
    {/each}
    {#each noted.target === target ? noted.notes : [] as m (m)}<p class="hint note" role="status">{m}</p>{/each}
    {#if problem}<p class="warn" role="alert">{problem}</p>{/if}
    {#each issues as m (m)}<p class="warn">{m}</p>{/each}
  {/if}
</fieldset>

<style>
  .sizes { display: grid; gap: 6px; }
  .field { display: grid; grid-template-columns: minmax(170px, 1fr) 90px minmax(100px, 160px) auto auto; gap: 8px; align-items: center; }
  .field.changed .name { font-weight: 600; }
  .range { display: grid; grid-template-columns: minmax(170px, 1fr) auto auto auto auto; gap: 8px; align-items: center; }
  .range.changed .name { font-weight: 600; }
  .range .end { display: flex; align-items: center; gap: 6px; font-size: 12px; color: var(--muted); }
  .range .end input { width: 70px; }
  .bars { display: grid; gap: 4px; }
  .bars p { margin: 0; }
  .bar { height: 10px; background: var(--border); border-radius: 5px; overflow: hidden; }
  .fill { display: block; height: 100%; }
  .fill.game { background: var(--muted); }
  .fill.mod { background: var(--accent, #4a9); }
  .baby { display: block; height: 100%; background: rgba(255, 255, 255, 0.35); }
</style>
