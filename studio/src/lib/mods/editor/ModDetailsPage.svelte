<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModSizeRangesDto } from '$lib/rpc/types.gen';
  import type { Range } from './colors';
  import type { ModDoc } from './modDoc.svelte';
  import RangeField from './RangeField.svelte';
  import { textureFacts } from './textureFacts.svelte';
  import { formatBytes } from './textures';

  let { doc }: { doc: ModDoc } = $props();
  const session = getSession();
  const tab = getTab();
  const facts = $derived(textureFacts(session, doc));
  $effect(() => facts.load());
  let name = $state('');
  let version = $state('');
  let author = $state('');
  let description = $state('');

  // Follows saves, undo and outside changes.
  $effect(() => {
    const d = doc.detail;
    if (!d) return;
    name = d.name;
    version = d.version;
    author = d.author ?? '';
    description = d.description ?? '';
  });

  /** The cooked pack in the workspace's mod folder (install writes it too). */
  const packLine = $derived.by(() => {
    const f = facts.current;
    if (!f) return '';
    if (f.pack === 'current') return `textures.tpack in the mod's folder is up to date (${formatBytes(f.packBytes ?? 0)}).`;
    if (f.pack === 'stale') return `textures.tpack in the mod's folder is out of date (${formatBytes(f.packBytes ?? 0)}): cook or install again.`;
    return "Not cooked yet: cooking or installing writes textures.tpack into the mod's folder.";
  });

  async function cook() {
    const r = await session.runJob('mods.cookTextures', { id: doc.id }, `Cook ${doc.id}'s textures`, tab);
    if (r) tab.info(r.message);
    facts.reload();
  }

  // The genetic size ranges (the Nursery's size slider, and the offspring of breeding) and the size limit; the server replaces all three together.
  let sizeRanges = $state.raw<ModSizeRangesDto | null>(null);
  $effect(() => {
    void doc.detail?.revision;
    void tab.quietly(() => session.rpc.call('mods.sizeRanges', { id: doc.id })).then((r) => {
      if (r) sizeRanges = r;
    });
  });
  const asRange = (v: number[] | null): Range | null => (v ? { min: v[0], max: v[1] } : null);
  const gameRange = (v: number[]): Range => ({ min: v[0], max: v[1] });
  const pair = (r: Range | null) => (r ? [r.min, r.max] : null);
  // The limit's absolute bounds (as mod.json takes them): its minimum within 0.01–0.05, its maximum within 2–20.
  const LIMIT = { lowestMin: 0.01, highestMin: 0.05, lowestMax: 2, highestMax: 20 };
  let limitProblem = $state('');
  /** One end of the size limit from its number field; a number outside that end's bounds is not saved. */
  function setLimitEnd(end: 'min' | 'max', text: string) {
    const now = sizeRanges?.limit;
    if (!now) return;
    const n = Number(text);
    const [lo, hi] = end === 'min' ? [LIMIT.lowestMin, LIMIT.highestMin] : [LIMIT.lowestMax, LIMIT.highestMax];
    if (text.trim() === '' || !Number.isFinite(n) || n < lo || n > hi) {
      limitProblem = `Size limit: the ${end === 'min' ? 'minimum' : 'maximum'} must be from ${lo} to ${hi}.`;
      return;
    }
    limitProblem = '';
    void saveRange('limit', end === 'min' ? { min: n, max: now[1] } : { min: now[0], max: n });
  }
  async function saveRange(which: 'nursery' | 'breeding' | 'limit', r: Range | null) {
    const now = sizeRanges;
    if (!now || (r && r.min > r.max)) return;
    const next = { nursery: now.nursery, breeding: now.breeding, limit: now.limit, [which]: pair(r) };
    const shown = { ...now, ...next };
    sizeRanges = shown;
    // Not saved: back to the saved ranges, unless a reload came first.
    if (!(await doc.edit('mods.setSizeRanges', next)) && sizeRanges === shown) sizeRanges = now;
  }
  const text = (v: number[]) => `${v[0].toFixed(2)} – ${v[1].toFixed(2)}`;

  async function save() {
    const d = doc.detail;
    if (!d) return;
    if (name.trim() === d.name && version.trim() === d.version && author.trim() === (d.author ?? '') && description.trim() === (d.description ?? '')) return;
    await doc.edit('mods.setDetails', { name, version, author: author.trim() || null, description: description.trim() || null });
  }
</script>

<section class="card details">
  <h2>Mod details</h2>
  <label>Name <input bind:value={name} onchange={save} /></label>
  <label>Version <input bind:value={version} onchange={save} /></label>
  <label>Author <input bind:value={author} placeholder="optional" onchange={save} /></label>
  <label>Description <textarea rows="3" bind:value={description} placeholder="One or two sentences about the mod (optional)" onchange={save}></textarea></label>
  {#if facts.current && facts.current.textures.length > 0}
    <label>Default texture compression
      <select value={facts.current.compression} onchange={(e) => doc.edit('mods.setCompression', { file: null, mode: e.currentTarget.value })}>
        <option value="auto">Match the game (DXT1/DXT5)</option>
        <option value="bc7">BC7 — best quality, twice DXT1's memory</option>
      </select>
    </label>
    <p class="hint">Every texture uses this unless you pick its own: the box under each texture on its skin or replacement page.</p>
    <p class="hint">Textures in game: about {formatBytes(facts.current.totalBytes)}. Tyrant compresses them when it installs or exports the mod (BC7 is compressed on the graphics card, a few seconds per texture, and takes longer without one; unchanged textures are reused).</p>
    <div class="cook">
      <p class="hint">{packLine}</p>
      <button onclick={cook} disabled={session.busy}>Cook textures</button>
    </div>
  {/if}
  <p class="hint">Id: <code>{doc.id}</code> — the mod's folder name and its key in the game, so it cannot change.</p>
  {#each doc.detail?.usesSpeciesFrom ?? [] as use (use.modId)}
    <p class="hint">Uses species from: {use.modName ?? use.modId}{#if !use.installed || !use.enabled} <span class="warn">({use.installed ? 'off' : 'not installed'} — {use.skins} {use.skins === 1 ? 'skin' : 'skins'} skipped)</span>{/if}</p>
  {/each}
</section>

{#if sizeRanges}
  <section class="card details">
    <h2>Size ranges</h2>
    <RangeField label="Nursery size slider range" baseLabel="Game's range" value={asRange(sizeRanges.nursery)} min={0.05} max={20} fallback={gameRange(sizeRanges.gameNursery)} onChange={(r) => saveRange('nursery', r)} />
    <p class="hint">The game's own Customize size slider in the Nursery covers this range. Random eggs keep the game's range. Game: {text(sizeRanges.gameNursery)}</p>
    <RangeField label="Breeding size range" baseLabel="Game's range" value={asRange(sizeRanges.breeding)} min={0.05} max={20} fallback={gameRange(sizeRanges.gameBreeding)} onChange={(r) => saveRange('breeding', r)} />
    <p class="hint">Game: {text(sizeRanges.gameBreeding)}</p>
    <div class="limit" role="group" aria-label="Size limit">
      <span class="name">Size limit</span>
      <label class="base"><input type="checkbox" checked={sizeRanges.limit === null}
        onchange={(e) => { limitProblem = ''; void saveRange('limit', e.currentTarget.checked ? null : gameRange(sizeRanges!.gameLimit)); }} /> Game's limit</label>
      {#if sizeRanges.limit}
        <label class="end">Minimum <input type="number" aria-label="Size limit minimum" min={LIMIT.lowestMin} max={LIMIT.highestMin} step="0.01"
          value={sizeRanges.limit[0]} onchange={(e) => setLimitEnd('min', e.currentTarget.value)} /></label>
        <label class="end">Maximum <input type="number" aria-label="Size limit maximum" min={LIMIT.lowestMax} max={LIMIT.highestMax} step="0.5"
          value={sizeRanges.limit[1]} onchange={(e) => setLimitEnd('max', e.currentTarget.value)} /></label>
      {/if}
    </div>
    {#if limitProblem}<p class="warn" role="alert">{limitProblem}</p>{/if}
    <p class="hint">The game caps animals at 0.05×–2×. A wider limit lets sizes past 2× show in game; the game was tuned for normal sizes, so very big animals may clip, get stuck or frame badly.</p>
    <p class="hint">Minimum 0.01–0.05, maximum 2–20. With several mods on, the widest limit wins.</p>
  </section>
{/if}

<style>
  .details { display: grid; gap: 10px; max-width: 560px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  .cook { display: flex; gap: 12px; align-items: center; justify-content: space-between; }
  .cook p { margin: 0; }
  .limit { display: grid; grid-template-columns: 90px auto auto auto; align-items: center; gap: 10px; }
  .limit .name { color: var(--muted); }
  .limit .base { display: flex; gap: 4px; font-size: 12px; }
  .limit .end { display: flex; align-items: center; gap: 6px; font-size: 12px; }
  .limit .end input { width: 70px; }
  input, textarea { font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px; }
</style>
