<script lang="ts">
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
  import { BREEDING_PATHS as B, DIFFICULTY, FECUNDITY, MATING_SYSTEMS, MONTHS, PARENTING, SIT_ON_EGGS, breedingIssues, type Option } from './breeding';
  import { flagList, flagText } from './speciesIdentity';

  /**
   * A species' breeding and lifecycle settings, with plain names, from its full animal record: `values` is the record's edits
   * (path → text), shared with the Full animal record and saved with the species. `valid` is false while a value can't load in the game.
   */
  let { fields, values = $bindable(), valid = $bindable(true) }: { fields: ModAnimalFieldDto[]; values: Record<string, string>; valid?: boolean } = $props();
  const uid = $props.id();

  const SYSTEM_OPTIONS: Option[] = MATING_SYSTEMS.map(([v, label]) => [v, label]);

  const byPath = $derived(new Map(fields.filter((f) => f.editable).map((f) => [f.path, f])));
  const has = (path: string) => byPath.has(path);
  const valueAt = (path: string) => values[path] ?? byPath.get(path)?.value ?? '';
  const orNull = (path: string) => (has(path) ? valueAt(path) : null);
  const shown = $derived(Object.values(B).some((p) => byPath.has(p)));

  const months = $derived(flagList(valueAt(B.months), MONTHS));
  function toggleMonth(month: string, on: boolean) {
    const rest = months.filter((m) => m !== month);
    values[B.months] = flagText(on ? [...rest, month] : rest, MONTHS);
  }

  const issues = $derived(breedingIssues({
    months: has(B.months) ? months : null, system: orNull(B.system), parenting: orNull(B.parenting),
    gestation: orNull(B.gestation), ideal: orNull(B.ideal), largest: orNull(B.largest), mostEggs: orNull(B.mostEggs),
  }));
  $effect(() => {
    valid = issues.errors.length === 0;
  });

  const systemDescription = $derived(MATING_SYSTEMS.find(([v]) => v === valueAt(B.system))?.[2]);
  const idOf = (path: string) => `${uid}-${path.replace(/[^a-zA-Z0-9]/g, '-')}`;

  /** The options, plus the record's own value when it isn't one of them, so nothing is silently changed. */
  const withCurrent = (options: readonly Option[], current: string): readonly Option[] =>
    current === '' || options.some(([v]) => v === current) ? options : [...options, [current, current]];
</script>

{#snippet pick(path: string, label: string, options: readonly Option[], hint?: string)}
  {#if has(path)}
    <div class="field">
      <label>{label}
        <select value={valueAt(path)} aria-describedby={hint ? idOf(path) : undefined} onchange={(e) => (values[path] = e.currentTarget.value)}>
          {#each withCurrent(options, valueAt(path)) as [v, text] (v)}<option value={v}>{text}</option>{/each}
        </select>
      </label>
      {#if hint}<span class="hint" id={idOf(path)}>{hint}</span>{/if}
    </div>
  {/if}
{/snippet}

{#snippet number(path: string, label: string, hint: string, step = 'any')}
  {#if has(path)}
    <div class="field">
      <label>{label}
        <input type="number" min="0" {step} value={valueAt(path)} aria-describedby={idOf(path)}
          oninput={(e) => { if (!e.currentTarget.validity.badInput) values[path] = e.currentTarget.value; }} />
      </label>
      <span class="hint" id={idOf(path)}>Template: {byPath.get(path)?.templateValue}{hint ? ` · ${hint}` : ''}</span>
    </div>
  {/if}
{/snippet}

{#if shown}
  <section class="breeding" aria-label="Breeding">
    <h3>Breeding</h3>
    <p class="hint">When and how the species breeds and raises its young: the Breeding & lifecycle part of the game's Social tab.</p>
    {#if has(B.months)}
      <div class="months" role="group" aria-label="Breeding months">
        <span class="label">Breeding months</span>
        <div class="ticks">
          {#each MONTHS as month (month)}
            <label class="tick"><input type="checkbox" checked={months.includes(month)} onchange={(e) => toggleMonth(month, e.currentTarget.checked)} /> {month}</label>
          {/each}
        </div>
      </div>
    {/if}
    <div class="grid">
      {@render pick(B.system, 'Mating system', SYSTEM_OPTIONS, systemDescription)}
      {@render pick(B.fecundity, 'Fertility', FECUNDITY, 'How likely mating gives young, against an average species.')}
      {@render number(B.gestation, 'Gestation (months)', 'The game rounds it for display.')}
      {@render number(B.ideal, 'Ideal clutch', '', '1')}
      {@render number(B.largest, 'Largest clutch', '', '1')}
      {@render number(B.mostEggs, 'Most eggs laid', '0 uses the largest clutch. Above 0, the game makes up to this many eggs instead (0–16).', '1')}
      {@render pick(B.parenting, 'Who raises the babies', PARENTING)}
      {@render number(B.familyCohesion, 'Babies stay close to family (0–1)', "The game's Clutch cohesion.")}
      {@render pick(B.difficulty, 'Egg survival difficulty', DIFFICULTY, "Not shown in the game's panels, but used.")}
      {@render pick(B.sitOnEggs, 'Sits on eggs', SIT_ON_EGGS, "Not shown in the game's panels, but used. Only matters when the species lays eggs in a nest.")}
    </div>
    {#if issues.errors.length || issues.warnings.length}
      <div role="alert">
        {#each issues.errors as problem (problem)}<p class="error">{problem}</p>{/each}
        {#each issues.warnings as problem (problem)}<p class="warning">{problem}</p>{/each}
      </div>
    {/if}
  </section>
{/if}

<style>
  .breeding { display: grid; gap: 10px; margin-top: 12px; }
  h3, p { margin: 0; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  .field, label { display: grid; gap: 4px; color: var(--muted); }
  select, input[type='number'] { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .months { display: grid; gap: 6px; }
  .label { color: var(--muted); }
  .ticks { display: flex; flex-wrap: wrap; gap: 6px 16px; }
  label.tick { display: flex; align-items: center; gap: 6px; color: var(--text); }
  .hint { font-size: 12px; }
  .warning { color: var(--warn-text); font-size: 13px; }
  .error { color: var(--error-text); font-size: 13px; font-weight: 600; }
</style>
