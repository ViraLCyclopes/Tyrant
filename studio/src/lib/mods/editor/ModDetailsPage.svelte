<script lang="ts">
  import type { ModDoc } from './modDoc.svelte';

  let { doc }: { doc: ModDoc } = $props();
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
  <p class="hint">Id: <code>{doc.id}</code> — the mod's folder name and its key in the game, so it cannot change.</p>
</section>

<style>
  .details { display: grid; gap: 10px; max-width: 560px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  input, textarea { font: inherit; color: var(--text); background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: 6px 8px; }
</style>
