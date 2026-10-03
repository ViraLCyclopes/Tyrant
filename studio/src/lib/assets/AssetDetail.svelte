<script lang="ts">
  import JsonTree from '$lib/data/JsonTree.svelte';
  import { formatBytes } from '$lib/format';
  import type { AssetDetails } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import AssetPreviewPanel from './AssetPreviewPanel.svelte';

  let { ref, onOpen }: { ref: string; onOpen: (ref: string) => void } = $props();

  const session = getSession();
  let details = $state<AssetDetails | null>(null);
  let sequence = 0;

  $effect(() => {
    void load(ref);
  });

  async function load(target: string) {
    const mine = ++sequence;
    details = null;
    const r = await session.quietly(() => session.rpc.call('assets.get', { ref: target }));
    if (mine === sequence) details = r; // a later pick wins
  }

  async function copy(label: string, text: string) {
    await session.platform.copy(text);
    session.notice = `${label} copied.`;
  }
</script>

{#if details}
  {@const asset = details.asset}
  <h2>{asset.name || '(unnamed)'}</h2>
  <dl>
    <dt>Type</dt><dd>{asset.type}{asset.script ? ` (${asset.script})` : ''}</dd>
    <dt>Size</dt><dd>{formatBytes(details.byteSize)}</dd>
    <dt>Bundle</dt><dd class="path">{asset.bundle}</dd>
  </dl>

  <section class="keys" aria-label="Keys">
    <h3>Keys</h3>
    {#if asset.containerPath}
      <div class="key">
        <span class="label">Addressables path</span><code>{asset.containerPath}</code>
        <button onclick={() => copy('Addressables path', asset.containerPath!)}>Copy Addressables path</button>
      </div>
    {/if}
    {#if asset.guid}
      <div class="key">
        <span class="label">GUID</span><code>{asset.guid}</code>
        <button onclick={() => copy('GUID', asset.guid!)}>Copy GUID</button>
      </div>
    {/if}
    <div class="key">
      <span class="label">Reference</span><code>{asset.ref}</code>
      <button onclick={() => copy('Reference', asset.ref)}>Copy reference</button>
    </div>
  </section>

  <AssetPreviewPanel {asset} />

  {#if details.references.length}
    <section aria-label="References">
      <h3>References ({details.references.length})</h3>
      <ul class="references">
        {#each details.references as reference, i (i)}
          <li>
            <span class="field">{reference.field}</span>
            {#if reference.ref}
              <button class="link" onclick={() => onOpen(reference.ref!)}>{reference.name || reference.ref}</button>
              <span class="hint">{reference.type}</span>
            {:else}
              <span class="hint">{reference.external}</span>
            {/if}
          </li>
        {/each}
      </ul>
    </section>
  {/if}

  <section aria-label="Fields">
    <h3>Fields</h3>
    <JsonTree value={details.fields} />
  </section>
{:else}
  <p class="hint">Loading…</p>
{/if}

<style>
  .key { display: grid; grid-template-columns: 130px 1fr auto; gap: 8px; align-items: center; margin-bottom: 6px; }
  .key code { word-break: break-all; }
  .label { color: var(--muted); }
  .references { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
  .field { font-family: var(--mono); color: var(--accent); margin-right: 8px; }
</style>
