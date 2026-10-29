import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModSkinDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import SkinOrderList from './SkinOrderList.svelte';

const species = 'tyrant.red-spot.new-animal';
const base = modDetail().skins[0];
const skin = (id: string, name: string, over: Partial<ModSkinDto> = {}): ModSkinDto => ({ ...base, id, key: `red-spot/${id}`, name, species, ...over });
const skins = [skin('start', 'Mine', { starting: true }), skin('brown', 'Brown', { group: 'Earthy' }), skin('chaos', 'Chaos Effect')];

function setup(order = ['start', 'brown', 'chaos']) {
  const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
  const doc = { id: 'red-spot', edit: vi.fn(async () => true) };
  const props = { doc, speciesKey: species, order, skins, onOpenSkin: vi.fn(), onMakeDefault: vi.fn() };
  renderWith(SkinOrderList, session, props);
  return props;
}

/** The skins' name buttons, top to bottom. */
const shown = () => screen.getAllByRole('button', { name: /^(Mine|Brown|Chaos Effect)$/ }).map((b) => b.textContent?.trim());

describe('SkinOrderList', () => {
  it('lists the skins in picker order; the starting skin is tagged, the others can be made the default', async () => {
    const props = setup(['brown', 'start', 'chaos']);
    expect(shown()).toEqual(['Brown', 'Mine', 'Chaos Effect']);
    expect(screen.getByText(/· starting skin/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Make Mine the default' })).toBeNull();
    expect(screen.getByText(/· Earthy/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Make Brown the default' }));
    expect(props.onMakeDefault).toHaveBeenCalledWith('brown', 'Brown');
    await fireEvent.click(screen.getByRole('button', { name: 'Chaos Effect' }));
    expect(props.onOpenSkin).toHaveBeenCalledWith('chaos');
  });

  it('Move up and Move down save the moved order; the ends are disabled', async () => {
    const props = setup();
    expect(screen.getByRole('button', { name: 'Move Mine up' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move Chaos Effect down' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move Mine down' })).toBeEnabled();
    await fireEvent.click(screen.getByRole('button', { name: 'Move Brown down' }));
    expect(props.doc.edit).toHaveBeenLastCalledWith('mods.setSkinOrder', { species, order: ['start', 'chaos', 'brown'] });
  });

  it('Alt+ArrowUp on a skin moves it up', async () => {
    const props = setup();
    await fireEvent.keyDown(screen.getByRole('button', { name: 'Chaos Effect' }), { key: 'ArrowUp', altKey: true });
    expect(props.doc.edit).toHaveBeenLastCalledWith('mods.setSkinOrder', { species, order: ['start', 'chaos', 'brown'] });
  });

  it('a move shows at once while it saves, then follows the order again', async () => {
    const props = setup();
    let settle = (_: boolean) => {};
    props.doc.edit.mockImplementation(() => new Promise<boolean>((resolve) => (settle = resolve)));
    await fireEvent.click(screen.getByRole('button', { name: 'Move Chaos Effect up' }));
    expect(shown()).toEqual(['Mine', 'Chaos Effect', 'Brown']);
    settle(false); // the save failed: the order did not change
    await waitFor(() => expect(shown()).toEqual(['Mine', 'Brown', 'Chaos Effect']));
  });

  describe('dragging', () => {
    /** Presses on a skin, moves past the threshold, and lets go over `over` (clientY 10 is its lower half: jsdom rects are 0 high). */
    async function drag(from: HTMLElement, over: Element | null, { y = 10, cancel = false } = {}) {
      const original = document.elementFromPoint;
      document.elementFromPoint = () => over;
      try {
        await fireEvent.pointerDown(from, { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
        await fireEvent.pointerMove(window, { clientX: 5, clientY: 30, pointerId: 1 });
        await fireEvent.pointerMove(window, { clientX: 5, clientY: y, pointerId: 1 });
        if (cancel) await fireEvent.keyDown(window, { key: 'Escape' });
        await fireEvent.pointerUp(window, { clientX: 5, clientY: y, pointerId: 1 });
        await fireEvent.click(from); // the browser's click after the press; a drag must not open the skin
      } finally {
        document.elementFromPoint = original;
      }
    }

    it('dropping on the lower half of a skin puts the dragged one after it and saves', async () => {
      const props = setup();
      await drag(screen.getByRole('button', { name: 'Mine' }), screen.getByRole('button', { name: 'Chaos Effect' }));
      expect(props.doc.edit).toHaveBeenLastCalledWith('mods.setSkinOrder', { species, order: ['brown', 'chaos', 'start'] });
      expect(props.onOpenSkin).not.toHaveBeenCalled();
    });

    it('dropping on the upper half puts it before that skin', async () => {
      const props = setup();
      await drag(screen.getByRole('button', { name: 'Chaos Effect' }), screen.getByRole('button', { name: 'Mine' }), { y: -10 });
      expect(props.doc.edit).toHaveBeenLastCalledWith('mods.setSkinOrder', { species, order: ['chaos', 'start', 'brown'] });
    });

    it('the list shows the moved order while dragging', async () => {
      setup();
      const original = document.elementFromPoint;
      document.elementFromPoint = () => screen.getByRole('button', { name: 'Chaos Effect' });
      try {
        await fireEvent.pointerDown(screen.getByRole('button', { name: 'Mine' }), { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
        await fireEvent.pointerMove(window, { clientX: 40, clientY: 10, pointerId: 1 });
        expect(shown()).toEqual(['Brown', 'Chaos Effect', 'Mine']);
        await fireEvent.pointerUp(window, { clientX: 40, clientY: 10, pointerId: 1 });
      } finally {
        document.elementFromPoint = original;
      }
    });

    it('a press without moving opens the skin; Escape or a drop outside the list cancels', async () => {
      const props = setup();
      await fireEvent.pointerDown(screen.getByRole('button', { name: 'Brown' }), { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
      await fireEvent.pointerUp(window, { clientX: 6, clientY: 6, pointerId: 1 });
      await fireEvent.click(screen.getByRole('button', { name: 'Brown' }));
      expect(props.onOpenSkin).toHaveBeenCalledWith('brown');

      await drag(screen.getByRole('button', { name: 'Mine' }), screen.getByRole('button', { name: 'Chaos Effect' }), { cancel: true });
      expect(shown()).toEqual(['Mine', 'Brown', 'Chaos Effect']);
      await drag(screen.getByRole('button', { name: 'Mine' }), document.body);
      expect(shown()).toEqual(['Mine', 'Brown', 'Chaos Effect']);
      expect(props.doc.edit).not.toHaveBeenCalled();
      expect(props.onOpenSkin).toHaveBeenCalledTimes(1);
    });
  });
});
