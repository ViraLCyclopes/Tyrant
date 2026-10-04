import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModDetail, ModSoundDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import SoundPage from './SoundPage.svelte';

const EVENT = 'event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp/Vox/TheroLarge_VoxSocialCall';
const sound = (over: Partial<ModSoundDto> = {}): ModSoundDto => ({
  event: EVENT, name: 'Social call', group: 'Calls', species: 'Carcharodontosaurus', skin: null, files: ['sounds/roar-1a2b3c4d.wav'], volume: 1, agePitch: 1, ...over,
});

function setup(s = sound()) {
  const platform = new FakePlatform();
  const session = new Session(new FakeRpc(), platform, memoryStore());
  const doc = { id: 'carch-voice', detail: { dir: 'D:\\ws\\mods\\carch-voice', revision: 'r1', sounds: [s] } as unknown as ModDetail, edit: vi.fn(async () => true) };
  const onRemoved = vi.fn();
  const onScopeChanged = vi.fn();
  renderWith(SoundPage, session, { doc, sound: s, onRemoved, onScopeChanged });
  return { doc, platform, onRemoved, onScopeChanged };
}

describe('SoundPage', () => {
  it('shows the sound, who hears it and its files', () => {
    setup();

    expect(screen.getByRole('heading', { name: 'Sound: Social call' })).toBeInTheDocument();
    expect(screen.getByText(EVENT)).toBeInTheDocument();
    expect(screen.getByText('roar-1a2b3c4d.wav')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Only one species' })).toBeChecked();
  });

  it('saves the volume and the baby pitch when a slider is let go', async () => {
    const { doc } = setup();

    await fireEvent.change(screen.getByRole('slider', { name: 'Volume' }), { target: { value: '1.5' } });
    await fireEvent.change(screen.getByRole('slider', { name: 'Baby pitch' }), { target: { value: '0.25' } });

    expect(doc.edit).toHaveBeenCalledWith('mods.setSound', { event: EVENT, species: 'Carcharodontosaurus', skin: null, volume: 1.5 });
    expect(doc.edit).toHaveBeenCalledWith('mods.setSound', { event: EVENT, species: 'Carcharodontosaurus', skin: null, agePitch: 0.25 });
  });

  it('moves the replacement to everyone and follows it', async () => {
    const { doc, onScopeChanged } = setup();

    await fireEvent.click(screen.getByRole('radio', { name: 'Everyone' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    expect(doc.edit).toHaveBeenCalledWith('mods.setSound', { event: EVENT, species: 'Carcharodontosaurus', skin: null, forEveryone: true });
    await waitFor(() => expect(onScopeChanged).toHaveBeenCalledWith(null, null));
  });

  it('moves the replacement to one skin', async () => {
    const { doc, onScopeChanged } = setup(sound({ species: null }));

    await fireEvent.click(screen.getByRole('radio', { name: 'Only one skin' }));
    await fireEvent.input(screen.getByRole('combobox', { name: 'Skin' }), { target: { value: 'carch-voice/scarred' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    expect(doc.edit).toHaveBeenCalledWith('mods.setSound', { event: EVENT, species: null, skin: null, newSkin: 'carch-voice/scarred' });
    await waitFor(() => expect(onScopeChanged).toHaveBeenCalledWith(null, 'carch-voice/scarred'));
  });

  it('adds files and removes one by saving the new list', async () => {
    const { doc, platform } = setup(sound({ files: ['sounds/a-11111111.wav', 'sounds/b-22222222.ogg'] }));
    platform.fileLists.push(['E:\\new\\c.flac']);

    await fireEvent.click(screen.getByRole('button', { name: 'Add files…' }));
    await waitFor(() =>
      expect(doc.edit).toHaveBeenCalledWith('mods.replaceSound', {
        event: EVENT, species: 'Carcharodontosaurus', skin: null,
        files: ['D:\\ws\\mods\\carch-voice\\sounds\\a-11111111.wav', 'D:\\ws\\mods\\carch-voice\\sounds\\b-22222222.ogg', 'E:\\new\\c.flac'],
      }),
    );
    await fireEvent.click(screen.getByRole('button', { name: 'Remove a-11111111.wav' }));

    expect(doc.edit).toHaveBeenLastCalledWith('mods.replaceSound', {
      event: EVENT, species: 'Carcharodontosaurus', skin: null, files: ['D:\\ws\\mods\\carch-voice\\sounds\\b-22222222.ogg'],
    });
  });

  it('keeps the last file', () => {
    setup();

    expect(screen.getByRole('button', { name: 'Remove roar-1a2b3c4d.wav' })).toBeDisabled();
  });

  it('Remove stops replacing the sound', async () => {
    const { doc, onRemoved } = setup();

    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    expect(doc.edit).toHaveBeenCalledWith('mods.removeSound', { event: EVENT, species: 'Carcharodontosaurus', skin: null });
    await waitFor(() => expect(onRemoved).toHaveBeenCalled());
  });
});
