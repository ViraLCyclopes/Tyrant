<script lang="ts">
  import type { Range } from './colors';

  /** A random range (two handles) or a fixed number (Fixed); null = from the base skin. Saves on release. */
  let {
    label,
    value,
    min,
    max,
    step = 0.01,
    disabled = false,
    fallback,
    onChange,
  }: { label: string; value: Range | null; min: number; max: number; step?: number; disabled?: boolean; fallback: Range; onChange: (v: Range | null) => void } =
    $props();

  const fixed = $derived(!!value && value.min === value.max);

  function setLow(n: number) {
    if (!value) return;
    onChange(fixed ? { min: n, max: n } : { min: Math.min(n, value.max), max: value.max });
  }

  function setHigh(n: number) {
    if (value) onChange({ min: value.min, max: Math.max(n, value.min) });
  }

  function setFixed(on: boolean) {
    if (!value) return;
    onChange(on ? { min: value.min, max: value.min } : { min: value.min, max: Math.min(max, value.min + 0.1) });
  }
</script>

<div class="field" role="group" aria-label={label}>
  <span class="label">{label}</span>
  <label class="base"><input type="checkbox" checked={value === null} {disabled} onchange={(e) => onChange(e.currentTarget.checked ? null : { ...fallback })} /> From base skin</label>
  {#if value}
    <div class="range">
      <input type="range" aria-label="{label} from" {min} {max} {step} value={value.min} {disabled} onchange={(e) => setLow(Number(e.currentTarget.value))} />
      {#if !fixed}<input type="range" aria-label="{label} to" {min} {max} {step} value={value.max} {disabled} onchange={(e) => setHigh(Number(e.currentTarget.value))} />{/if}
      <span class="numbers">{fixed ? value.min.toFixed(2) : `${value.min.toFixed(2)} – ${value.max.toFixed(2)}`}</span>
      <label class="fixed"><input type="checkbox" aria-label="{label} fixed" checked={fixed} {disabled} onchange={(e) => setFixed(e.currentTarget.checked)} /> Fixed</label>
    </div>
  {/if}
</div>

<style>
  .field { display: grid; grid-template-columns: 90px auto 1fr; align-items: center; gap: 10px; }
  .label { color: var(--muted); }
  .base, .fixed { font-size: 12px; color: var(--muted); }
  .range { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .range input[type='range'] { width: 120px; }
  .numbers { font-family: var(--mono); font-size: 12px; color: var(--muted); min-width: 90px; }
</style>
