<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
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
    <label>Texture compression
      <select value={facts.current.compression} onchange={(e) => doc.edit('mods.setCompression', { file: null, mode: e.currentTarget.value })}>
        <option value="auto">Match the game (DXT1/DXT5)</option>
        <option value="bc7">BC7 — best quality, twice DXT1's memory</option>
      </select>
    </label>
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

<style>
  .details { display: grid; gap: 10px; max-width: 560px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  .cook { display: flex; gap: 12px; align-items: center; justify-content: space-between; }
  .cook p { margin: 0; }
  input, textarea { font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px; }
</style>
