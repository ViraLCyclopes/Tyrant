<script lang="ts">
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
  import { DIETS, DIET_ORDER, NICHE_AGES, NICHE_ORDER, NICHE_ROLES, NICHE_SIZES, dietWarnings, formatFlags, nicheProblems, parseFlags } from './speciesIdentity';

  /**
   * A species' food and niche, with plain names, from its full animal record: `values` is the record's edits (path → text),
   * shared with the Full animal record and saved with the species. `hasPredation` is whether its behaviour has a "Hunts prey" rule.
   */
  let { fields, values = $bindable(), valid = $bindable(true), hasPredation }: { fields: ModAnimalFieldDto[]; values: Record<string, string>; valid?: boolean; hasPredation: boolean } = $props();

  const dietPath = '/diet';
  const byPath = $derived(new Map(fields.filter((f) => f.editable).map((f) => [f.path, f])));
  const hasDiet = $derived(byPath.has(dietPath));
  const ages = $derived(NICHE_AGES.filter(([path]) => byPath.has(path)));
  const flagsAt = (path: string) => parseFlags(values[path] ?? byPath.get(path)?.value ?? '');

  const diet = $derived(flagsAt(dietPath));
  const niches = $derived(ages.map(([path]) => flagsAt(path)));
  const problems = $derived.by(() => {
    const found: string[] = [];
    if (hasDiet && !diet.some((d) => DIETS.some(([name]) => name === d))) found.push('Tick at least one food.');
    ages.forEach(([, label], i) => found.push(...nicheProblems(label, niches[i])));
    return found;
  });
  const warnings = $derived(dietWarnings(hasDiet ? diet : [], niches, hasPredation));
  $effect(() => {
    valid = problems.length === 0;
  });

  /** Writes `path` back with `flag` added or removed, keeping every other flag (Kibble, unknown ones) as it was. */
  function toggle(path: string, order: readonly string[], flag: string, on: boolean) {
    const rest = flagsAt(path).filter((f) => f !== flag);
    values[path] = formatFlags(on ? [...rest, flag] : rest, order);
  }
  function setSize(path: string, size: string) {
    values[path] = formatFlags([...flagsAt(path).filter((f) => !NICHE_SIZES.includes(f)), size], NICHE_ORDER);
  }
  const sizeOf = (flags: string[]) => NICHE_SIZES.find((s) => flags.includes(s)) ?? '';
</script>

{#if hasDiet || ages.length}
  <section class="diet" aria-label="Diet and niche">
    <h3>Diet and niche</h3>
    <p class="hint">What the species eats and where it fits among the others. These are the same values as /diet and /niches in the Full animal record.</p>
    {#if hasDiet}
      <h4>Food</h4>
      <div class="row">
        {#each DIETS as [name, label] (name)}
          <label class="check"><input type="checkbox" checked={diet.includes(name)} onchange={(e) => toggle(dietPath, DIET_ORDER, name, e.currentTarget.checked)} />{label}</label>
        {/each}
      </div>
      <span class="hint">Template: {byPath.get(dietPath)?.templateValue}</span>
    {/if}
    {#if ages.length}
      <h4>Niche</h4>
      <div class="grid">
        {#each ages as [path, age], i (path)}
          <div class="field">
            <label>{age} size
              <select value={sizeOf(niches[i])} onchange={(e) => setSize(path, e.currentTarget.value)}>
                {#if sizeOf(niches[i]) === ''}<option value="" disabled>Pick a size</option>{/if}
                {#each NICHE_SIZES as size (size)}<option value={size}>{size}</option>{/each}
              </select>
            </label>
            {#each NICHE_ROLES as [role, label] (role)}
              <label class="check"><input type="checkbox" aria-label={`${age}: ${label}`} checked={niches[i].includes(role)} onchange={(e) => toggle(path, NICHE_ORDER, role, e.currentTarget.checked)} />{label}</label>
            {/each}
            <span class="hint">Template: {byPath.get(path)?.templateValue}</span>
          </div>
        {/each}
      </div>
    {/if}
    {#if problems.length}<div role="alert">{#each problems as problem (problem)}<p class="error">{problem}</p>{/each}</div>{/if}
    {#if warnings.length}<div>{#each warnings as warning (warning)}<p class="warning">{warning}</p>{/each}</div>{/if}
  </section>
{/if}

<style>
  .diet { display: grid; gap: 10px; margin-top: 12px; }
  h3, h4, p { margin: 0; }
  h4 { font-size: 13px; color: var(--muted); }
  .row { display: flex; flex-wrap: wrap; gap: 14px; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  .field, label { display: grid; gap: 4px; color: var(--muted); }
  .check { display: flex; align-items: center; gap: 6px; }
  select { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .hint { font-size: 12px; }
  .error { color: var(--error-text); font-size: 13px; }
  .warning { color: var(--warn-text); font-size: 13px; }
</style>
