<script lang="ts" module>
  /** Where a new species' starting skin gets its PNGs: a copy of a base-game skin, or the modder's own diffuse PNGs. */
  export type StartingChoice =
    | { kind: 'copy'; species: string; skin: number; name: string }
    | { kind: 'png'; male: string | null; female: string | null; name: string };

  /** A copy of the template's first skin, unnamed. */
  export const freshChoice = (template: string): StartingChoice => ({ kind: 'copy', species: template, skin: 0, name: '' });

  /** The starting-skin fields of mods.addSpecies / mods.addStartingSkin. */
  export function startParams(c: StartingChoice) {
    const startName = c.name.trim() || null;
    return c.kind === 'copy'
      ? { startFrom: c.species, startSkin: c.skin, startPng: null, startPngFemale: null, startName }
      : { startFrom: null, startSkin: null, startPng: c.male, startPngFemale: c.female, startName };
  }

  /** The template skin the species' game object is copied from: the chosen one when copying from the template, else its first. */
  export const startingSkinOf = (c: StartingChoice, template: string) => (c.kind === 'copy' && c.species === template ? c.skin : 0);
</script>

<script lang="ts">
  import type { SpeciesSkinsRow } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  /** templates: the base-game regular animals (mods.species without vivarium ones); template: the new species' template. */
  let { templates, template, speciesName = '', value, onChange }: {
    templates: SpeciesSkinsRow[]; template: string; speciesName?: string; value: StartingChoice; onChange: (v: StartingChoice) => void;
  } = $props();
  const session = getSession();

  const sources = $derived([...templates.filter((t) => t.speciesId === template), ...templates.filter((t) => t.speciesId !== template)]);
  const row = $derived(value.kind === 'copy' ? templates.find((t) => t.speciesId === value.species) : undefined);
  const skin = $derived(value.kind === 'copy' ? row?.skins.find((s) => s.index === value.skin) : undefined);
  const placeholder = $derived(value.kind === 'copy' ? (skin?.name ?? '') : speciesName);

  async function pick(sex: 'male' | 'female') {
    if (value.kind !== 'png') return;
    const png = await session.platform.openFile(`Choose the ${sex} diffuse PNG for the starting skin`, ['png']);
    if (png && value.kind === 'png') onChange({ ...value, [sex]: png });
  }
</script>

<fieldset class="start">
  <legend>Starting skin</legend>
  <div class="kinds">
    <label class="row"><input type="radio" name="start-kind" checked={value.kind === 'copy'} onchange={() => onChange({ ...freshChoice(template), name: value.name })} /> Copy a skin</label>
    <label class="row"><input type="radio" name="start-kind" checked={value.kind === 'png'} onchange={() => onChange({ kind: 'png', male: null, female: null, name: value.name })} /> My own PNGs</label>
  </div>
  {#if value.kind === 'copy'}
    {@const copy = value}
    <div class="grid">
      <label>Copy from
        <select value={copy.species} onchange={(e) => onChange({ ...copy, species: e.currentTarget.value, skin: 0 })}>
          {#each sources as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}</option>{/each}
        </select>
      </label>
      <label>Starting skin
        <select value={copy.skin} onchange={(e) => onChange({ ...copy, skin: Number(e.currentTarget.value) })}>
          {#each row?.skins ?? [] as s (s.index)}<option value={s.index}>{s.name}</option>{/each}
        </select>
      </label>
    </div>
    {#if copy.species !== template}
      <p class="hint">These textures fit {copy.species}'s model; give this skin that model (Models on its page) or they will not line up.</p>
    {/if}
    {#each ['male', 'female'] as const as sex (sex)}
      {#if skin && !skin[sex]}<p class="hint">This skin has no {sex} textures; {sex} animals wear the template's skin.</p>{/if}
    {/each}
  {:else}
    <div class="files">
      <button type="button" onclick={() => pick('male')}>Choose male diffuse PNG…</button>
      <span class="hint">{value.male ?? 'None chosen (needed)'}</span>
      <button type="button" onclick={() => pick('female')}>Choose female diffuse PNG… (optional)</button>
      <span class="hint">{value.female ?? 'The male one'}</span>
    </div>
    <p class="hint">The normal, extra, pattern and fur maps come from the template skin; replace them on the skin's page.</p>
  {/if}
  <label>Starting skin name <input value={value.name} {placeholder} oninput={(e) => onChange({ ...value, name: e.currentTarget.value })} /></label>
</fieldset>

<style>
  .start { border: 1px solid var(--border); border-radius: var(--radius); padding: 12px; display: grid; gap: 10px; }
  .kinds { display: flex; gap: 16px; }
  .row { display: flex; gap: 6px; align-items: center; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  .files { display: grid; grid-template-columns: auto 1fr; gap: 8px 12px; align-items: center; }
  .hint { font-size: 12px; margin: 0; }
</style>
