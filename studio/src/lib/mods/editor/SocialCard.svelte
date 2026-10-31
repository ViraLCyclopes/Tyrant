<script lang="ts">
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
  import { cohesionWord, desiresHerd, exhibitSize, expectedGroups, groupKind, type SocialNumbers } from './socialPreview';

  /**
   * A species' social settings, with plain names, from its full animal record: `values` is the record's edits (path → text),
   * shared with the Full animal record and saved with the species.
   */
  let { fields, values = $bindable(), valid = $bindable(true), ownSocialInfo = false }: {
    fields: ModAnimalFieldDto[]; values: Record<string, string>; valid?: boolean;
    /** The species (or its template) has its own social info text, which the game shows instead of its group sentence. */
    ownSocialInfo?: boolean;
  } = $props();
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
    { path: `${social}/shared/rangeOverlapAllowance`, label: 'Crowding allowed (0–1)', min: 0, max: 1, hint: "The Paddock tab's Crowding bar. More allowed crowding makes the expected exhibit smaller." },
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
  // What the game's panel will show, worked out from the values above (unsaved edits included).
  const previewPaths: Record<keyof SocialNumbers, string> = {
    malesMin: groupSize[0].path, malesMax: groupSize[1].path, femalesMin: groupSize[2].path, femalesMax: groupSize[3].path,
    spaceMale: others[0].path, spaceFemale: others[1].path, overlap: others[2].path, cohesion: others[3].path,
  };
  /** A preview number: the edit, else the record's value; NaN when blank or not a number. */
  const previewNumber = (path: string) => {
    const text = (values[path] ?? byPath.get(path)?.value ?? '').trim();
    return text === '' ? NaN : Number(text);
  };
  const numbers = $derived(Object.fromEntries(Object.entries(previewPaths).map(([k, p]) => [k, previewNumber(p)])) as SocialNumbers);
  /** The preview's own counts: they start at the fewest males and females, as the game's calculator does, and never change the species. */
  let malesText = $state<string | null>(null);
  let femalesText = $state<string | null>(null);
  // Another species (or a reload after Save or Reset) starts the counts at its own fewest again.
  $effect.pre(() => {
    void fields;
    malesText = null;
    femalesText = null;
  });
  const males = $derived(malesText === null ? numbers.malesMin : malesText.trim() === '' ? NaN : Number(malesText));
  const females = $derived(femalesText === null ? numbers.femalesMin : femalesText.trim() === '' ? NaN : Number(femalesText));
  /** `text` of the numbers it needs, or a dash when one of them is blank. */
  const shownIf = (needed: number[], text: () => string) => (needed.every(Number.isFinite) ? text() : '—');
  const counts = $derived([numbers.malesMin, numbers.malesMax, numbers.femalesMin, numbers.femalesMax]);
  const GROUP_TEXT = {
    none: 'It says the species forms no groups.', couples: 'It says the species lives in couples.', females: 'It says the species forms female-only groups.',
    males: 'It says the species forms male-only groups.', mixed: 'It says the species forms mixed groups of males and females.',
  } as const;

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
    {#if hasGroupSize}
      <section class="preview" aria-label="What the game will show">
        <h4>What the game will show</h4>
        <p class="hint">Worked out from the values above, the way the game's Exhibit and Social tabs do. Change the counts to try other groups; they don't change the species.</p>
        <div class="counts">
          <label>Males <input type="number" min="0" step="1" value={malesText ?? (Number.isFinite(numbers.malesMin) ? numbers.malesMin : '')} oninput={(e) => (malesText = e.currentTarget.value)} /></label>
          <label>Females <input type="number" min="0" step="1" value={femalesText ?? (Number.isFinite(numbers.femalesMin) ? numbers.femalesMin : '')} oninput={(e) => (femalesText = e.currentTarget.value)} /></label>
        </div>
        <dl>
          <div><dt>Expected exhibit size</dt><dd>{shownIf([numbers.spaceMale, numbers.spaceFemale, numbers.overlap, males, females], () => `${Math.round(exhibitSize(numbers, males, females))} m²`)}</dd></div>
          <div><dt>Expected social groups</dt><dd>{shownIf([...counts, males, females], () => `${expectedGroups(numbers, males, females).groups}`)}</dd></div>
          <div><dt>Expected nomads</dt><dd>{shownIf([...counts, males, females], () => `${expectedGroups(numbers, males, females).nomads}`)}</dd></div>
          <div><dt>Group cohesion</dt><dd>{shownIf([numbers.malesMax, numbers.femalesMax, desiresHerd(numbers) ? numbers.cohesion : 0], () => cohesionWord(numbers))}</dd></div>
          <div><dt>Herd leader</dt><dd>{shownIf([numbers.malesMax, numbers.femalesMax], () => (desiresHerd(numbers) ? (values[leaderPath] ?? leader?.value ?? '—') : 'None'))}</dd></div>
        </dl>
        <p class="hint">
          {#if ownSocialInfo}The Paleopedia shows the species' own social info text about its groups.
          {:else}{shownIf(counts, () => GROUP_TEXT[groupKind(numbers)])}{/if}
        </p>
      </section>
    {/if}
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
  .preview { display: grid; gap: 8px; border-top: 1px solid var(--border); padding-top: 10px; }
  .counts { display: grid; grid-template-columns: repeat(2, minmax(0, 140px)); gap: 14px; }
  dl { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 6px 14px; margin: 0; }
  dl div { display: flex; justify-content: space-between; gap: 8px; }
  dt { color: var(--muted); }
  dd { margin: 0; color: var(--text); }
</style>
