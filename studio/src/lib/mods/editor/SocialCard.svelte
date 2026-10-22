<script lang="ts">
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';

  /**
   * A species' social settings, with plain names, from its full animal record: `values` is the record's edits (path → text),
   * shared with the Full animal record and saved with the species.
   */
  let { fields, values = $bindable() }: { fields: ModAnimalFieldDto[]; values: Record<string, string> } = $props();

  type Item = { path: string; label: string; min?: number; max?: number; step?: string };
  const social = '/brain/social';
  const groupSize: Item[] = [
    { path: `${social}/adult/socialMaleCountRange/x`, label: 'Males, fewest', min: 0, step: '1' },
    { path: `${social}/adult/socialMaleCountRange/y`, label: 'Males, most', min: 0, step: '1' },
    { path: `${social}/adult/socialFemaleCountRange/x`, label: 'Females, fewest', min: 0, step: '1' },
    { path: `${social}/adult/socialFemaleCountRange/y`, label: 'Females, most', min: 0, step: '1' },
  ];
  const others: Item[] = [
    { path: `${social}/shared/individualRangeMale`, label: 'Space per male (m²)', min: 16, max: 32000 },
    { path: `${social}/shared/individualRangeFemale`, label: 'Space per female (m²)', min: 16, max: 32000 },
    { path: `${social}/shared/rangeOverlapAllowance`, label: 'Range overlap (0–1)', min: 0, max: 1 },
    { path: `${social}/adult/socialPositionalCohesion`, label: 'Stays close together (0–1)', min: 0, max: 1 },
    { path: `${social}/adult/socialStateCohesion`, label: 'Acts together (0–1)', min: 0, max: 1 },
    { path: '/brain/needs/adult/native/socialIsolationTime', label: 'Time it can be alone', min: 0 },
    { path: `${social}/adult/socialCommunicationInterval/x`, label: 'Calls to the group, soonest', min: 0 },
    { path: `${social}/adult/socialCommunicationInterval/y`, label: 'Calls to the group, latest', min: 0 },
  ];
  const leaderPath = `${social}/adult/socialLeaderType`;

  const byPath = $derived(new Map(fields.filter((f) => f.editable).map((f) => [f.path, f])));
  const shown = (items: Item[]) => items.filter((item) => byPath.has(item.path));
  const hasGroupSize = $derived(shown(groupSize).length === groupSize.length);
  const leader = $derived(byPath.get(leaderPath));
  const problems = $derived(hasGroupSize ? (['Males', 'Females'] as const).flatMap((sex, i) => {
    const fewest = Number(values[groupSize[i * 2].path]);
    const most = Number(values[groupSize[i * 2 + 1].path]);
    return fewest > most ? [`${sex}: fewest (${fewest}) is more than most (${most}).`] : [];
  }) : []);
</script>

{#snippet number(item: Item)}
  <div class="field">
    <label>{item.label}
      <input type="number" min={item.min} max={item.max} step={item.step ?? 'any'} value={values[item.path] ?? ''}
        oninput={(e) => (values = { ...values, [item.path]: e.currentTarget.value })} />
    </label>
    <span class="hint">Template: {byPath.get(item.path)?.templateValue}</span>
  </div>
{/snippet}

{#if byPath.size && (shown(others).length || leader || hasGroupSize)}
  <section class="social" aria-label="Social">
    <h3>Social</h3>
    <p class="hint">How the species lives with others. These are the same values as under brain → social in the Full animal record.</p>
    <h4>Group size</h4>
    {#if hasGroupSize}
      <div class="grid">{#each groupSize as item (item.path)}{@render number(item)}{/each}</div>
      {#each problems as problem (problem)}<p class="warning" role="alert">{problem}</p>{/each}
    {:else}
      <p class="hint">This workspace's data dump has no group sizes (an older dump wrote them empty): run the data dump again from the Workspace tab to edit them.</p>
    {/if}
    <h4>Space and behaviour</h4>
    <div class="grid">
      {#each shown(others) as item (item.path)}{@render number(item)}{/each}
      {#if leader}
        <div class="field">
          <label>Leader
            <select value={values[leaderPath] ?? leader.value} onchange={(e) => (values = { ...values, [leaderPath]: e.currentTarget.value })}>
              {#each ['Either', 'Patriarch', 'Matriarch'] as choice (choice)}<option value={choice}>{choice}</option>{/each}
            </select>
          </label>
          <span class="hint">Template: {leader.templateValue}</span>
        </div>
      {/if}
    </div>
  </section>
{/if}

<style>
  .social { display: grid; gap: 10px; margin-top: 12px; }
  h3, h4, p { margin: 0; }
  h4 { font-size: 13px; color: var(--muted); }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  .field, label { display: grid; gap: 4px; color: var(--muted); }
  input, select { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .hint { font-size: 12px; }
  .warning { color: var(--warn-text); font-size: 13px; }
</style>
