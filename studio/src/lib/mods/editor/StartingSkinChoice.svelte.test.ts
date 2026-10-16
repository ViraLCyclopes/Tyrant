import { fireEvent, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { SpeciesSkinsRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import StartingSkinChoice, { freshChoice, startParams, startingSkinOf, type StartingChoice } from './StartingSkinChoice.svelte';

const templates: SpeciesSkinsRow[] = [
  { speciesId: 'Allosaurus Anax', vivarium: false, skins: [{ index: 0, name: 'Default', male: true, female: true }, { index: 1, name: 'Albino', male: true, female: true }], prefabGuid: null },
  { speciesId: 'Carcharodontosaurus', vivarium: false, skins: [{ index: 0, name: 'Base', male: true, female: true }, { index: 1, name: 'Alt 1', male: true, female: false }], prefabGuid: null },
];

function setup(value: StartingChoice = freshChoice('Allosaurus Anax')) {
  const platform = new FakePlatform();
  const session = new Session(new FakeRpc(), platform, memoryStore());
  const props = $state({ templates, template: 'Allosaurus Anax', speciesName: 'Mirabilis', value, onChange: (v: StartingChoice) => { props.value = v; } });
  renderWith(StartingSkinChoice, session, props);
  return { props, platform };
}

describe('StartingSkinChoice', () => {
  it('copies the template skin by default, and another species only with the model note', async () => {
    const { props } = setup();
    expect(screen.getByRole('radio', { name: 'Copy a skin' })).toBeChecked();
    expect(within(screen.getByRole('combobox', { name: 'Copy from' })).getAllByRole('option').map((o) => o.textContent)).toEqual(['Allosaurus Anax', 'Carcharodontosaurus']);
    expect(screen.queryByText(/These textures fit/)).toBeNull();

    await fireEvent.change(screen.getByRole('combobox', { name: 'Copy from' }), { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Starting skin' }), { target: { value: '1' } });

    expect(props.value).toMatchObject({ kind: 'copy', species: 'Carcharodontosaurus', skin: 1 });
    expect(screen.getByText("These textures fit Carcharodontosaurus's model; give this skin that model (Models on its page) or they will not line up.")).toBeInTheDocument();
    expect(screen.getByText("This skin has no female textures; female animals wear the template's skin.")).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Starting skin name' })).toHaveAttribute('placeholder', 'Alt 1');
  });

  it('own PNGs pick a male diffuse and an optional female one', async () => {
    const { props, platform } = setup();
    await fireEvent.click(screen.getByRole('radio', { name: 'My own PNGs' }));
    platform.files.push('C:/art/male.png');
    await fireEvent.click(screen.getByRole('button', { name: 'Choose male diffuse PNG…' }));
    await screen.findByText('C:/art/male.png');

    expect(props.value).toEqual({ kind: 'png', male: 'C:/art/male.png', female: null, name: '' });
    expect(screen.getByRole('button', { name: 'Choose female diffuse PNG… (optional)' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Starting skin name' })).toHaveAttribute('placeholder', 'Mirabilis');
  });

  it('turns a choice into the RPC fields', () => {
    expect(startParams({ kind: 'copy', species: 'Carcharodontosaurus', skin: 1, name: ' Mine ' }))
      .toEqual({ startFrom: 'Carcharodontosaurus', startSkin: 1, startPng: null, startPngFemale: null, startName: 'Mine' });
    expect(startParams({ kind: 'png', male: 'C:/m.png', female: null, name: '' }))
      .toEqual({ startFrom: null, startSkin: null, startPng: 'C:/m.png', startPngFemale: null, startName: null });
    expect(startingSkinOf({ kind: 'copy', species: 'Allosaurus Anax', skin: 1, name: '' }, 'Allosaurus Anax')).toBe(1);
    expect(startingSkinOf({ kind: 'copy', species: 'Carcharodontosaurus', skin: 1, name: '' }, 'Allosaurus Anax')).toBe(0);
  });
});
