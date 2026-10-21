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
  const clamp = (n: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, n));
  const inLimit = (r: Range): Range => ({ min: clamp(r.min, LIMIT.lowestMin, LIMIT.highestMin), max: clamp(r.max, LIMIT.lowestMax, LIMIT.highestMax) });
  async function saveRange(which: 'nursery' | 'breeding' | 'limit', r: Range | null) {
    const now = sizeRanges;
    if (!now || (r && r.min > r.max)) return;
    if (r && which === 'limit') r = inLimit(r);
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
    <RangeField label="Size limit" baseLabel="Game's limit" value={asRange(sizeRanges.limit)} min={LIMIT.lowestMin} max={LIMIT.highestMax} fixable={false} fallback={gameRange(sizeRanges.gameLimit)} onChange={(r) => saveRange('limit', r)} />
    <p class="hint">The game caps animals at 0.05×–2×. A wider limit lets sizes past 2× show in game; the game was tuned for normal sizes, so very big animals may clip, get stuck or frame badly.</p>
    <p class="hint">Minimum 0.01–0.05, maximum 2–20. With several mods on, the widest limit wins.</p>
  </section>
{/if}

<style>
  .details { display: grid; gap: 10px; max-width: 560px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  .cook { display: flex; gap: 12px; align-items: center; justify-content: space-between; }
  .cook p { margin: 0; }
  input, textarea { font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px; }
</style>
