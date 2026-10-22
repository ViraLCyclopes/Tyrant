<script lang="ts">
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';

  /**
   * A species' social settings, with plain names, from its full animal record: `values` is the record's edits (path → text),
   * shared with the Full animal record and saved with the species.
   */
  let { fields, values = $bindable(), valid = $bindable(true) }: { fields: ModAnimalFieldDto[]; values: Record<string, string>; valid?: boolean } = $props();
  const uid = $props.id();

  type Item = { path: string; label: string; min?: number; max?: number; step?: string; hint?: string };
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
    { path: `${social}/adult/socialPositionalCohesion`, label: 'Adults stay close together (0–1)', min: 0, max: 1 },
    { path: `${social}/adult/socialStateCohesion`, label: 'Adults act together (0–1)', min: 0, max: 1 },
    { path: '/brain/needs/adult/native/socialIsolationTime', label: 'Adults can be alone for', min: 0, hint: "0 means it never gets lonely; the game's editor uses 0–1200." },
    { path: `${social}/adult/socialCommunicationInterval/x`, label: 'Adults call the group, soonest', min: 0 },
    { path: `${social}/adult/socialCommunicationInterval/y`, label: 'Adults call the group, latest', min: 0 },
  ];
  const callSoonest = others[6].path;
  const callLatest = others[7].path;
  const leaderPath = `${social}/adult/socialLeaderType`;

  const byPath = $derived(new Map(fields.filter((f) => f.editable).map((f) => [f.path, f])));
  const shown = (items: Item[]) => items.filter((item) => byPath.has(item.path));
  const hasGroupSize = $derived(shown(groupSize).length === groupSize.length);
  /** The dump wrote group sizes as {} (an older dump), rather than the species' template not being in the dump. */
  const olderDump = $derived(fields.some((f) => /\/social(Male|Female)CountRange$/.test(f.path) && f.kind === 'object'));
  const leader = $derived(byPath.get(leaderPath));
  /** A number typed in a box, or null when the box is empty or not a number. */
  const numberIn = (path: string) => {
    const text = (values[path] ?? '').trim();
    return text === '' || !Number.isFinite(Number(text)) ? null : Number(text);
  };
  const problems = $derived.by(() => {
    const found: string[] = [];
    if (hasGroupSize) {
      for (const item of groupSize)
        if (!/^\d+$/.test((values[item.path] ?? '').trim())) found.push(`${item.label} must be a whole number of 0 or more.`);
      (['Males', 'Females'] as const).forEach((sex, i) => {
        const fewest = numberIn(groupSize[i * 2].path);
        const most = numberIn(groupSize[i * 2 + 1].path);
        if (fewest !== null && most !== null && fewest > most) found.push(`${sex}: fewest (${fewest}) is more than most (${most}).`);
      });
    }
    const soonest = numberIn(callSoonest);
    const latest = numberIn(callLatest);
    if (soonest !== null && latest !== null && soonest > latest) found.push(`Calls: soonest (${soonest}) is later than latest (${latest}).`);
    return found;
  });
  // Whole numbers are a must (the game cannot load a species with 2.5 animals); the order warnings are only warnings.
  $effect(() => {
    valid = !problems.some((p) => p.includes('whole number'));
  });
  const idOf = (path: string) => `${uid}-${path.replace(/[^a-zA-Z0-9]/g, '-')}`;
</script>

{#snippet number(item: Item)}
  <div class="field">
    <label>{item.label}
      <input type="number" min={item.min} max={item.max} step={item.step ?? 'any'} value={values[item.path] ?? ''} aria-describedby={idOf(item.path)}
        oninput={(e) => { if (!e.currentTarget.validity.badInput) values = { ...values, [item.path]: e.currentTarget.value }; }} />
    </label>
    <span class="hint" id={idOf(item.path)}>Template: {byPath.get(item.path)?.templateValue}{item.hint ? ` · ${item.hint}` : ''}</span>
  </div>
{/snippet}

{#if byPath.size && (shown(others).length || leader || hasGroupSize)}
  <section class="social" aria-label="Social">
    <h3>Social</h3>
    <p class="hint">How the species lives with others. These are the same values as under brain → social in the Full animal record.</p>
    <h4>Group size</h4>
    {#if hasGroupSize}
      <div class="grid">{#each groupSize as item (item.path)}{@render number(item)}{/each}</div>
    {:else if olderDump}
      <p class="hint">This workspace's data dump has no group sizes (an older dump wrote them empty): run the data dump again from the Workspace tab to edit them.</p>
    {:else}
      <p class="hint">The data dump has no group sizes for this species' template.</p>
    {/if}
    <h4>Space and behaviour</h4>
    <div class="grid">
      {#each shown(others) as item (item.path)}{@render number(item)}{/each}
      {#if leader}
        <div class="field">
          <label>Leader
            <select value={values[leaderPath] ?? leader.value} aria-describedby={idOf(leaderPath)} onchange={(e) => (values = { ...values, [leaderPath]: e.currentTarget.value })}>
              {#each ['Either', 'Patriarch', 'Matriarch'] as choice (choice)}<option value={choice}>{choice}</option>{/each}
            </select>
          </label>
          <span class="hint" id={idOf(leaderPath)}>Template: {leader.templateValue}</span>
        </div>
      {/if}
    </div>
    {#if problems.length}<div role="alert">{#each problems as problem (problem)}<p class="warning">{problem}</p>{/each}</div>{/if}
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
