<script lang="ts">
  import type { ToolDef } from '$lib/shell/registry';

  let { tools, onOpen }: { tools: ToolDef[]; onOpen: (id: string) => void } = $props();
  let expanded = $state(false);
</script>

<nav class="dock" aria-label="Tools">
  <div class="row">
    {#each tools as tool (tool.id)}
      <button class="tool" class:planned={tool.status === 'planned'} disabled={tool.status === 'planned'} title={tool.blurb} onclick={() => onOpen(tool.id)}>
        <!-- eslint-disable-next-line svelte/no-at-html-tags -- only the inline SVG constants from shell/icons.ts -->
        <span class="icon" aria-hidden="true">{@html tool.icon}</span>
        <span class="name">{tool.name}</span>
        {#if tool.status === 'planned'}<span class="soon">Not built yet</span>{/if}
      </button>
    {/each}
    <button class="more" aria-expanded={expanded} onclick={() => (expanded = !expanded)}>{expanded ? 'Fewer details' : 'All tools'}</button>
  </div>
  {#if expanded}
    <ul class="catalogue">
      {#each tools as tool (tool.id)}
        <li><strong>{tool.name}</strong> — {tool.blurb}{#if tool.status === 'planned'} <span class="soon">(not built yet)</span>{/if}</li>
      {/each}
    </ul>
  {/if}
</nav>

<style>
  .dock { background: color-mix(in srgb, var(--panel) 88%, transparent); border: 1px solid var(--border); border-radius: 14px; padding: 10px 12px; backdrop-filter: blur(6px); max-width: 100%; }
  .row { display: flex; gap: 8px; flex-wrap: wrap; justify-content: center; }
  .tool { display: grid; justify-items: center; gap: 4px; width: 104px; padding: 10px 6px; background: var(--panel-2); }
  .tool.planned { opacity: 0.45; }
  .icon :global(svg) { width: 28px; height: 28px; }
  .soon { font-size: 11px; color: var(--muted); }
  .more { align-self: center; }
  .catalogue { margin: 10px 4px 0; padding-left: 18px; color: var(--muted); }
</style>
