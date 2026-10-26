<script lang="ts">
  import { untrack } from 'svelte';
  import type { ModAnimalFieldDto, ModBehaviourProfileDto, ModBehaviourRuleDto, ModSpeciesBehaviourDto } from '$lib/rpc/types.gen';

  /**
   * A species' behaviour and fighting strength, with plain names, from its full animal record: `values` is the record's edits (path → text),
   * shared with the Full animal record and saved with the species. `behaviour` is its behaviour choice (the profile it starts from and
   * its rule ticks), null until the user changes either. `templateProfile` is the profile the template's brain uses.
   */
  let { fields, values = $bindable(), profiles, rules, templateProfile, behaviour = $bindable(), confirm }: {
    fields: ModAnimalFieldDto[]; values: Record<string, string>; profiles: ModBehaviourProfileDto[]; rules: ModBehaviourRuleDto[];
    templateProfile: string | null; behaviour: ModSpeciesBehaviourDto | null; confirm: (message: string) => Promise<boolean>;
  } = $props();

  type Option = readonly [value: string, label: string];
  const plain = (names: string[]): Option[] => names.map((n) => [n, n]);
  const CONFIDENCE = plain(['Skittish', 'Cautious', 'Average', 'Confident', 'Fearless']);
  const TEMPERAMENT = plain(['Passive', 'Defensive', 'Aggressive']);
  const CANNIBALISM: Option[] = [['None', 'None'], ['WhenStarving', 'When starving'], ['WhenHungry', 'When hungry']];
  const LEVELS = plain(['Lowest', 'VeryLow', 'Low', 'MediumLow', 'Average', 'MediumHigh', 'High', 'VeryHigh']);
  const COOPERATION = plain(['Individualistic', 'Gregarious', 'Cooperative', 'Unified', 'Devoted']);
  const CLADES = plain(['Theropoda', 'Sauropodomorpha', 'Thyreophora', 'Ornithopoda', 'Marginocephalia', 'Xenarthra', 'Afrotheria', 'Laurasiatheria', 'Primatomorpha', 'Glires', 'Marsupialia', 'Monotremes', 'Pseudosuchia', 'Pterosauria', 'StemArchosauromorpha', 'Lepidosauria', 'Pantestudines', 'Ichthyosauria', 'Sauropterygia', 'StemMammalia', 'Batrachomorpha', 'StemTetrapoda', 'Dipnomorpha', 'Actinistia', 'Actinopterygii', 'Chondrichthyes', 'Placodermi', 'Agnatha', 'Arthropoda']);

  const profilePath = '/brain/combat/shared/combatProfile/';
  const confidencePath = profilePath + 'confidenceLevel';
  const temperamentPath = profilePath + 'temperament';
  const cannibalismPath = profilePath + 'cannibalism';
  const combat = '/brain/combat/';
  const rangePath = combat + 'shared/aggressionRangeMult';
  const chancePath = combat + 'shared/cannibalismPropensity';
  const swimPath = combat + 'shared/canAttackWhileSwimming';
  const cladePath = '/clade';
  const strength: { age: string; dir: string }[] = [{ age: 'Adult', dir: 'adult' }, { age: 'Baby', dir: 'infant' }];

  const byPath = $derived(new Map(fields.filter((f) => f.editable).map((f) => [f.path, f])));
  const has = (path: string) => byPath.has(path);
  const valueAt = (path: string) => values[path] ?? byPath.get(path)?.value ?? '';

  const hasCatalogue = $derived(profiles.length > 0);
  // Start and ticks begin from the props, then belong to the user.
  let start = $state(untrack(() => behaviour?.startFrom ?? templateProfile ?? ''));
  let ticks = $state<string[]>(untrack(() => behaviour?.rules ?? profiles.find((p) => p.asset === templateProfile)?.rules ?? []));
  const startProfile = $derived(profiles.find((p) => p.asset === start));

  /** Ticked rules in priority order, then any the catalogue doesn't list. */
  const orderedTicks = (on: string[]) => [...rules.map((r) => r.asset).filter((a) => on.includes(a)), ...on.filter((a) => !rules.some((r) => r.asset === a))];
  const commit = () => { behaviour = { startFrom: start, rules: orderedTicks(ticks) }; };

  function toggle(asset: string, on: boolean) {
    ticks = on ? [...ticks, asset] : ticks.filter((a) => a !== asset);
    commit();
  }

  const profileValues = (p: ModBehaviourProfileDto): [string, string][] =>
    [[confidencePath, p.confidence], [temperamentPath, p.temperament], [cannibalismPath, p.cannibalism]];

  /** Whether the ticks or the three profile choices differ from what `p` gives. */
  function differsFrom(p: ModBehaviourProfileDto) {
    const same = p.rules.length === ticks.length && p.rules.every((a) => ticks.includes(a));
    return !same || profileValues(p).some(([path, v]) => has(path) && valueAt(path) !== v);
  }

  async function startFrom(e: Event & { currentTarget: HTMLSelectElement }) {
    const select = e.currentTarget;
    const next = profiles.find((p) => p.asset === select.value);
    if (!next) return;
    if (startProfile && differsFrom(startProfile)
      && !(await confirm(`Start from ${next.name}? This replaces your rule ticks and the confidence, temperament and cannibalism choices.`))) {
      select.value = start;
      return;
    }
    start = next.asset;
    ticks = [...next.rules];
    for (const [path, v] of profileValues(next)) if (has(path)) values[path] = v;
    commit();
  }

  /** The options, plus the record's own value when it isn't one of them, so nothing is silently changed. */
  const withCurrent = (options: readonly Option[], current: string): readonly Option[] =>
    current === '' || options.some(([v]) => v === current) ? options : [...options, [current, current]];
</script>

{#snippet pick(path: string, label: string, options: readonly Option[])}
  {#if has(path)}
    <label>{label}
      <select value={valueAt(path)} onchange={(e) => (values[path] = e.currentTarget.value)}>
        {#each withCurrent(options, valueAt(path)) as [v, text] (v)}<option value={v}>{text}</option>{/each}
      </select>
    </label>
  {/if}
{/snippet}

{#snippet number(path: string, label: string, min: number, max?: number)}
  {#if has(path)}
    <label>{label}
      <input type="number" {min} {max} step="any" value={valueAt(path)} oninput={(e) => (values[path] = e.currentTarget.value)} />
    </label>
  {/if}
{/snippet}

<section class="behaviour" aria-label="Behaviour">
  <h3>Behaviour</h3>
  {#if !hasCatalogue}
    <p class="hint">Behaviour profiles need a game data dump. Run Dump in Game first.</p>
  {:else}
    <label>Start from
      <select value={start} onchange={startFrom}>
        {#if !startProfile}<option value="" disabled selected>Pick a profile</option>{/if}
        {#each profiles as p (p.asset)}<option value={p.asset}>{p.name}{p.usedBy.length ? ` (used by ${p.usedBy.join(', ')})` : ''}</option>{/each}
      </select>
    </label>
    {#if !startProfile}<span class="hint">The template's profile isn't in your game data dump. Pick a profile to start from, then tick its rules.</span>{/if}
    <h4>Rules</h4>
    <div class="rules">
      {#each rules as r (r.asset)}
        <label class="check"><input type="checkbox" checked={ticks.includes(r.asset)} disabled={!startProfile} onchange={(e) => toggle(r.asset, e.currentTarget.checked)} />{r.name}{#if r.shownInGame} <span class="hint">shown in game</span>{/if}</label>
      {/each}
    </div>
  {/if}
  {#if has(confidencePath) || has(temperamentPath) || has(cannibalismPath)}
    <div class="grid">
      {@render pick(confidencePath, 'Confidence', CONFIDENCE)}
      {@render pick(temperamentPath, 'Temperament', TEMPERAMENT)}
      {@render pick(cannibalismPath, 'Cannibalism', CANNIBALISM)}
    </div>
  {/if}

  <h4>Fighting strength</h4>
  <div class="grid">
    {#each strength as { age, dir } (dir)}
      {@render pick(`${combat}${dir}/lethalityLevel`, `${age} lethality`, LEVELS)}
      {@render pick(`${combat}${dir}/defenseLevel`, `${age} defence`, LEVELS)}
      {@render pick(`${combat}${dir}/cooperationLevel`, `${age} cooperation`, COOPERATION)}
    {/each}
    {@render number(rangePath, 'Attack range ×', 0)}
    {@render number(chancePath, 'Cannibalism chance (0–1)', 0, 1)}
    {#if has(swimPath)}
      <label class="check"><input type="checkbox" checked={valueAt(swimPath) === 'true'} onchange={(e) => (values[swimPath] = e.currentTarget.checked ? 'true' : 'false')} />Can fight while swimming</label>
    {/if}
  </div>

  {#if has(cladePath)}
    <div class="grid">{@render pick(cladePath, 'Clade', CLADES)}</div>
    <span class="hint">Animals of the same clade count as relatives for some game checks, and it groups the species in the Nursery.</span>
  {/if}
</section>

<style>
  .behaviour { display: grid; gap: 10px; margin-top: 12px; }
  h3, h4, p { margin: 0; }
  h4 { font-size: 13px; color: var(--muted); }
  .rules { display: flex; flex-wrap: wrap; gap: 14px; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  .check { display: flex; align-items: center; gap: 6px; }
  select, input[type='number'] { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .hint { font-size: 12px; }
</style>
