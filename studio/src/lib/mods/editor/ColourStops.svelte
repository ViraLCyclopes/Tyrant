<script lang="ts">
  import { MAX_STOPS } from './colors';

  /**
   * One colour, or a gradient of up to `max`; null = from the base skin (left out of mod.json). Saves on release.
   * `baseLabel` names the null choice; null hides it (the caller offers its own).
   */
  let {
    label,
    value,
    disabled = false,
    max = MAX_STOPS,
    baseLabel = 'From base skin',
    onChange,
  }: { label: string; value: string[] | null; disabled?: boolean; max?: number; baseLabel?: string | null; onChange: (v: string[] | null) => void } = $props();

  function set(i: number, colour: string) {
    onChange((value ?? []).map((c, n) => (n === i ? colour.toLowerCase() : c)));
  }

  function remove(i: number) {
    if (value && value.length > 1) onChange(value.filter((_, n) => n !== i));
  }
</script>

<div class="field" role="group" aria-label={label}>
  <span class="label">{label}</span>
  {#if baseLabel !== null}
    <label class="base"><input type="checkbox" checked={value === null} {disabled} onchange={(e) => onChange(e.currentTarget.checked ? null : ['#808080'])} /> {baseLabel}</label>
  {:else}
    <span></span>
  {/if}
  {#if value}
    <div class="stops">
      {#each value as colour, i (i)}
        <span class="stop" role="presentation" oncontextmenu={(e) => { e.preventDefault(); remove(i); }}>
          <input type="color" aria-label="{label} colour {i + 1}" value={colour} {disabled} onchange={(e) => set(i, e.currentTarget.value)} />
          {#if value.length > 1}<button class="ghost x" aria-label="Remove {label} colour {i + 1}" {disabled} onclick={() => remove(i)}>✕</button>{/if}
        </span>
      {/each}
      <button class="add" aria-label="Add a {label} colour" disabled={disabled || value.length >= max} onclick={() => onChange([...value, value.at(-1) ?? '#808080'])}>+</button>
      {#if value.length > 1}<span class="gradient" style="background: linear-gradient(90deg, {value.join(', ')})" aria-hidden="true"></span>{/if}
    </div>
  {/if}
</div>

<style>
  .field { display: grid; grid-template-columns: 90px auto 1fr; align-items: center; gap: 10px; }
  .label { color: var(--muted); }
  .base { font-size: 12px; color: var(--muted); }
  .stops { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }
  .stop { position: relative; }
  input[type='color'] { width: 28px; height: 24px; padding: 0; border: 1px solid var(--border); border-radius: 4px; background: none; }
  .x { position: absolute; top: -8px; right: -8px; padding: 0 3px; font-size: 10px; }
  .add { padding: 2px 8px; }
  .gradient { width: 110px; height: 10px; border-radius: 3px; margin-left: 6px; }
</style>
