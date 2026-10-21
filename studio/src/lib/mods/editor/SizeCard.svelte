<script lang="ts">
  import type { ModSizesDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import { speciesExtremes, type Key as CurveKey, type Range } from './sizeMath';

  /**
   * One species' size changes: the adult size, the newborn's share of it, each sex's multiplier, and the species' genetic size
   * ranges (the Nursery slider's and breeding's), shown as percentages of this species' size with the sizes they give. A key
   * the mod leaves unset shows the game's value and Reset puts it back. The sex sizes multiply each skin's own, so theirs is
   * 1× (the skin's own shows as information only). The server replaces the whole set, so every save carries all six. The
   * size limit is automatic (it follows the sizes, up to 20×): there is nothing to set. A slider moves its number field, the
   * size bar, the lengths, the range results and the smallest/biggest line while it is dragged, and saves once, on release
   * (one undo step). The number fields show 3 decimals; an unedited value keeps its full precision.
   */
  let { doc, target }: { doc: ModDoc; target: string } = $props();
  const session = getSession();
  const tab = getTab();

  type Key = 'adult' | 'baby' | 'male' | 'female';
  type Field = { key: Key; label: string; min: number; max: number; slider: number; step: number };
  type RangeKey = 'nursery' | 'breeding';
  type Own = Record<Key, number | null> & Record<RangeKey, number[] | null>;
  type RangeRow = { key: RangeKey; label: string };
  // The genetic ranges take 0 < min <= max <= 20 (as sizes/<file>.json takes them); their fields show them as percentages.
  const RANGES: RangeRow[] = [
    { key: 'nursery', label: 'Nursery size slider range' },
    { key: 'breeding', label: 'Breeding size range' },
  ];
  const RANGE_MAX = 20;
  /** The smallest end a range field takes (0.5%, its step): its field's min and its check agree. */
  const RANGE_MIN = 0.005;
  // The number fields take what the files take (up to 20×; Check says when a species passes Tyrant's 20×). The sliders reach
  // 5×, or the saved value when it is higher, so their usual range stays easy to drag.
  const MAX = 20;
  const reach = (saved: number | null) => Math.min(MAX, Math.max(5, Math.ceil(saved ?? 0)));
  const fieldsFor = (o: Own): Field[] => [
    { key: 'adult', label: 'Adult size ×', min: 0.05, max: MAX, slider: reach(o.adult), step: 0.01 },
    { key: 'baby', label: 'Baby size (share of adult)', min: 0.01, max: 1, slider: 1, step: 0.01 },
    { key: 'male', label: 'Male size ×', min: 0.1, max: MAX, slider: reach(o.male), step: 0.01 },
    { key: 'female', label: 'Female size ×', min: 0.1, max: MAX, slider: reach(o.female), step: 0.01 },
  ];

  let loaded = $state.raw<ModSizesDto | null>(null);
  let failed = $state(false);
  let own = $state<Own>({ adult: null, baby: null, male: null, female: null, nursery: null, breeding: null });
  let problem = $state('');
  /** The slider being dragged and its value, shown everywhere until it is let go (and saved). */
  let dragging = $state<{ key: Key; value: number } | null>(null);
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
    adult: r.adult, baby: r.baby, male: r.male, female: r.female, nursery: r.nursery, breeding: r.breeding,
  });
  const ready = $derived(loaded?.target.toLowerCase() === target.toLowerCase() ? loaded : null);
  const fields = $derived(fieldsFor(own));

  // Unchanged values: adult, male and female are multipliers (1×); baby is the game's own share.
  const original = $derived<Record<Key, number>>({ adult: 1, baby: ready?.original.babyShare ?? 1, male: 1, female: 1 });
  const round = (n: number) => Number(n.toFixed(3));
  /** A size relative to a normal animal: one decimal from 1× up (13.2), two figures under it (0.28). */
  const times = (n: number) => String(n >= 1 ? Number(n.toFixed(1)) : Number(n.toPrecision(2)));
  const percent = (n: number) => round(n * 100);
  const skin = $derived(ready?.original.skins[0]);
  const sexHint = $derived(skin ? `game: ${round(skin.male)} male / ${round(skin.female)} female` : '');
  const shown = (key: Key) => (dragging?.key === key ? dragging.value : (own[key] ?? original[key]));

  let saves = 0;
  async function save(next: Own) {
    own = next;
    problem = '';
    const [mine, reads, saved, t] = [++saves, loads, ready, target];
    const ok = await doc.edit('mods.setSizes', { target: t, ...next });
    // Not saved (the edit failed, nothing changed on disk): back to what the mod has, unless a later save or a reload came first.
    if (!ok && mine === saves && reads === loads && saved) own = ownOf(saved);
  }

  // A range unset shows the game's (the base species', for a custom species its template's), and an edit starts from it.
  const gameOf = (key: RangeKey) => (key === 'nursery' ? ready?.gameNursery : ready?.gameBreeding) ?? [1, 1];
  const rangeShown = (key: RangeKey) => own[key] ?? gameOf(key);
  const pairText = (v: number[]) => `${percent(v[0])}% – ${percent(v[1])}%`;

  /** One end of a range from its percentage field (saved as a multiplier); outside 0.5% <= min <= max <= 2000% it is not saved. */
  function changeEnd(row: RangeRow, end: 0 | 1, text: string) {
    const now = rangeShown(row.key);
    const n = Number(text) / 100;
    const bad = end === 0 ? n < RANGE_MIN || n > now[1] : n < now[0] || n > RANGE_MAX;
    if (text.trim() === '' || !Number.isFinite(n) || bad) {
      problem =
        end === 0
          ? `${row.label}: the minimum must be from ${percent(RANGE_MIN)}% to the maximum (${percent(now[1])}%).`
          : `${row.label}: the maximum must be from the minimum (${percent(now[0])}%) to ${RANGE_MAX * 100}%.`;
      return;
    }
    void save({ ...own, [row.key]: end === 0 ? [n, now[1]] : [now[0], n] });
  }

  // What the ranges give and what the species reaches, counted live (a dragged slider too), relative to a normal base animal.
  const baseName = $derived(ready?.baseName ?? target);
  const rangeResult = (key: RangeKey) => {
    const [lo, hi] = rangeShown(key);
    return `→ ${times(shown('adult') * lo)}× to ${times(shown('adult') * hi)}× a normal ${baseName}`;
  };
  const reached = $derived(
    ready
      ? speciesExtremes({
          curve: ready.original.curve as CurveKey[], skins: ready.original.skins,
          adult: dragging?.key === 'adult' ? dragging.value : own.adult, baby: dragging?.key === 'baby' ? dragging.value : own.baby,
          male: dragging?.key === 'male' ? dragging.value : own.male, female: dragging?.key === 'female' ? dragging.value : own.female,
          nursery: own.nursery as Range | null, breeding: own.breeding as Range | null,
          gameIncubation: ready.gameNursery as Range, gameBreeding: ready.gameBreeding as Range,
        })
      : null,
  );

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
      {#if reached}<p class="hint reach">Smallest hatchling {times(reached.min)}×, biggest adult {times(reached.max)}× a normal {baseName}</p>{/if}
    </div>
    <p class="hint">The size limit is automatic: in game this species may reach the sizes set here, up to Tyrant's 20×. Past the game's normal 2×, very big animals may clip, get stuck or frame badly.</p>
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
        <label class="end">Minimum <input type="number" aria-label="{row.label} minimum" min={percent(RANGE_MIN)} max={percent(shownRange[1])} step="0.5"
          value={percent(shownRange[0])} onchange={(e) => changeEnd(row, 0, e.currentTarget.value)} />%</label>
        <label class="end">Maximum <input type="number" aria-label="{row.label} maximum" min={percent(shownRange[0])} max={RANGE_MAX * 100} step="0.5"
          value={percent(shownRange[1])} onchange={(e) => changeEnd(row, 1, e.currentTarget.value)} />%</label>
        <span class="hint result">{rangeResult(row.key)}</span>
        <span class="hint">{own[row.key] === null ? "game's" : `game: ${pairText(gameOf(row.key))}`}</span>
        {#if own[row.key] !== null}
          <button class="ghost" aria-label="Reset {row.label}" title="Back to the game's" onclick={() => save({ ...own, [row.key]: null })}>↺</button>
        {/if}
      </div>
      {#if row.key === 'nursery'}
        <p class="hint">The game's own Customize size slider in the Nursery covers this range for this species, as a percentage of its size. Random eggs keep the game's range.</p>
      {/if}
    {/each}
    {#if problem}<p class="warn" role="alert">{problem}</p>{/if}
    {#each issues as m (m)}<p class="warn">{m}</p>{/each}
  {/if}
</fieldset>

<style>
  .sizes { display: grid; gap: 6px; }
  .field { display: grid; grid-template-columns: minmax(170px, 1fr) 90px minmax(100px, 160px) auto auto; gap: 8px; align-items: center; }
  .field.changed .name { font-weight: 600; }
  .range { display: grid; grid-template-columns: minmax(170px, 1fr) auto auto auto auto auto; gap: 8px; align-items: center; }
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
