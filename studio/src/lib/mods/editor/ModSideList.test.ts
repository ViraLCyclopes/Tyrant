import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModDetail, ModSkinDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import ModSideList from './ModSideList.svelte';

const base = modDetail().skins[0];
const skin = (id: string, name: string, group: string | null = null): ModSkinDto => ({ ...base, id, key: `red-spot/${id}`, name, group });

function setup(detail: ModDetail = modDetail({ skins: [skin('a', 'Amber'), skin('c', 'Crimson', 'Jurassic Park'), skin('d', 'Dusk', 'Jurassic Park')] })) {
  const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
  const props = { detail, check: null, selection: { kind: 'details' as const }, onSelect: vi.fn(), onAddSkin: vi.fn(), onArrange: vi.fn(), onRemoveSkin: vi.fn() };
  renderWith(ModSideList, session, props);
  return { session, props };
}

/** The order the last arrange request builds from the current skins (the editor runs the builder on its latest detail). */
const lastOrder = (props: { detail: ModDetail; onArrange: ReturnType<typeof vi.fn> }) => props.onArrange.mock.lastCall![0](props.detail.skins);

describe('ModSideList skins', () => {
  it("tags a species' starting skin", () => {
    setup(modDetail({ skins: [{ ...skin('s', 'Mine'), starting: true }, skin('a', 'Amber')] }));
    expect(screen.getByRole('button', { name: 'Mine' })).toHaveTextContent('Starting skin');
    expect(screen.getByRole('button', { name: 'Amber' })).not.toHaveTextContent('Starting skin');
  });

  it('shows ungrouped skins, then each group under its heading with its species', () => {
    setup();
    expect(screen.getByRole('button', { name: 'Amber' })).toHaveTextContent('Carcharodontosaurus');
    expect(screen.getByRole('button', { name: 'Jurassic Park (2)' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'Crimson' })).toBeInTheDocument();
  });

  it('a group folds away and stays folded for this mod', async () => {
    const { session } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Jurassic Park (2)' }));
    expect(screen.queryByRole('button', { name: 'Crimson' })).toBeNull();
    expect(session.store.get('tyrant.mods.collapsedGroups.red-spot')).toBe('["Jurassic Park"]');
  });

  it("a skin's menu moves it and puts it in a group", async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Dusk' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move up' }));
    expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Amber' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move to Jurassic Park' }));
    expect(lastOrder(props)).toEqual([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
  });

  it('New group… asks for a name', async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Amber' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move to a new group…' }));
    const dialog = screen.getByRole('dialog', { name: 'New group' });
    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Group name' }), { target: { value: 'Isla Sorna' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));
    expect(lastOrder(props)).toEqual([
      { id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Isla Sorna' },
    ]);
  });

  it('Alt+Down moves the skin; Remove skin… asks the editor', async () => {
    const { props } = setup();
    await fireEvent.keyDown(screen.getByRole('button', { name: 'Crimson' }), { key: 'ArrowDown', altKey: true });
    expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Crimson' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Remove skin…' }));
    expect(props.onRemoveSkin).toHaveBeenCalledWith('c');
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
        await fireEvent.click(from); // the browser's click after the press; a drag must not select
      } finally {
        document.elementFromPoint = original;
      }
    }

    it('dropping a skin on the lower half of another puts it after that skin, in its group', async () => {
      const { props } = setup();
      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Dusk' }));
      expect(lastOrder(props)).toEqual([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
      expect(props.onSelect).not.toHaveBeenCalled();
    });

    it('dropping on the upper half puts it before that skin', async () => {
      const { props } = setup();
      await drag(screen.getByRole('button', { name: 'Dusk' }), screen.getByRole('button', { name: 'Crimson' }), { y: -10 });
      expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);
    });

    it('dropping on the Skins heading takes it out of its group; on a folded group heading puts it at that group end', async () => {
      const { props } = setup();
      await drag(screen.getByRole('button', { name: 'Dusk' }), screen.getByText(/^Skins \(/));
      expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: null }, { id: 'c', group: 'Jurassic Park' }]);

      await fireEvent.click(screen.getByRole('button', { name: 'Jurassic Park (2)' })); // fold it
      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Jurassic Park (2)' }));
      expect(lastOrder(props)).toEqual([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
    });

    /** The skins as the list shows them, top to bottom. */
    const shownSkins = () => screen.getAllByRole('button', { name: /^(Amber|Crimson|Dusk)$/ }).map((b) => b.getAttribute('aria-label'));

    it('while dragging, a copy follows the pointer and the list opens a gap where it lands', async () => {
      setup();
      const original = document.elementFromPoint;
      document.elementFromPoint = () => screen.getByRole('button', { name: 'Dusk' });
      try {
        await fireEvent.pointerDown(screen.getByRole('button', { name: 'Amber' }), { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
        await fireEvent.pointerMove(window, { clientX: 40, clientY: 10, pointerId: 1 });
        expect(document.querySelector('.ghost')).toHaveTextContent('Amber');
        expect(shownSkins()).toEqual(['Crimson', 'Dusk', 'Amber']); // live, before letting go
        await fireEvent.pointerUp(window, { clientX: 40, clientY: 10, pointerId: 1 });
        expect(document.querySelector('.ghost')).toBeNull();
      } finally {
        document.elementFromPoint = original;
      }
    });

    it('a drop shows the new order at once and keeps it while it saves; a failed save puts it back', async () => {
      const { props } = setup();
      let settle = () => {};
      props.onArrange.mockImplementation(() => new Promise<void>((resolve) => (settle = resolve)));
      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Dusk' }));
      expect(shownSkins()).toEqual(['Crimson', 'Dusk', 'Amber']);

      settle(); // the editor's detail did not change (the save failed): the list follows the detail again
      await waitFor(() => expect(shownSkins()).toEqual(['Amber', 'Crimson', 'Dusk']));
    });

    it('Alt+Down shows the move at once too', async () => {
      const { props } = setup();
      props.onArrange.mockImplementation(() => new Promise<void>(() => {}));
      await fireEvent.keyDown(screen.getByRole('button', { name: 'Crimson' }), { key: 'ArrowDown', altKey: true });
      expect(shownSkins()).toEqual(['Amber', 'Dusk', 'Crimson']);
    });

    it('a press without moving still selects; Escape or a drop outside the list cancels', async () => {
      const { props } = setup();
      await fireEvent.pointerDown(screen.getByRole('button', { name: 'Amber' }), { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
      await fireEvent.pointerUp(window, { clientX: 6, clientY: 6, pointerId: 1 });
      await fireEvent.click(screen.getByRole('button', { name: 'Amber' }));
      expect(props.onSelect).toHaveBeenCalledWith({ kind: 'skin', id: 'a' });

      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Dusk' }), { cancel: true });
      await drag(screen.getByRole('button', { name: 'Amber' }), document.body);
      expect(props.onArrange).not.toHaveBeenCalled();
      expect(props.onSelect).toHaveBeenCalledTimes(1);
    });
  });

  it("a group's menu renames and ungroups; + on a group adds into it", async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for group Jurassic Park' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Ungroup' }));
    expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'c', group: null }, { id: 'd', group: null }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Add a skin to Jurassic Park' }));
    expect(props.onAddSkin).toHaveBeenCalledWith('Jurassic Park');
    await fireEvent.click(screen.getByRole('button', { name: 'Add a skin' }));
    expect(props.onAddSkin).toHaveBeenLastCalledWith(null);
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
  });
});

describe('ModSideList prefab settings', () => {
  it('lists the prefab targets of the mod and selects one; + Add asks for the form', async () => {
    const { props } = setup(modDetail({ prefabTargets: [{ target: 'Spinosaurus', file: 'spinosaurus.json', fields: 2 }] }));
    expect(screen.getByText('Prefab settings (1)')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Spinosaurus' }));
    expect(props.onSelect).toHaveBeenCalledWith({ kind: 'prefab', target: 'Spinosaurus' });
    const onAddPrefab = vi.fn();
    cleanup();
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModSideList, session, { detail: modDetail(), check: null, selection: { kind: 'details' as const }, onSelect: vi.fn(), onAddSkin: vi.fn(), onAddPrefab });
    await fireEvent.click(screen.getByRole('button', { name: 'Add prefab settings' }));
    expect(onAddPrefab).toHaveBeenCalled();
  });

  it('also lists the selected target while it has no file yet', () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModSideList, session, { detail: modDetail(), check: null, selection: { kind: 'prefab' as const, target: 'Allosaurus' }, onSelect: vi.fn(), onAddSkin: vi.fn() });
    expect(screen.getByRole('button', { name: 'Allosaurus' })).toBeInTheDocument();
  });
});

describe('ModSideList species materials', () => {
  it('lists species materials in their own section, not under Models; + Add asks for the form', async () => {
    const onAddMaterial = vi.fn();
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    const onSelect = vi.fn();
    const detail = modDetail({ models: [], speciesMaterials: [{ species: 'Titanoboa', materialsJson: '{"Titanoboa":{"settings":{"_Smoothness":0.5}}}' }] });
    renderWith(ModSideList, session, { detail, check: null, selection: { kind: 'details' as const }, onSelect, onAddSkin: vi.fn(), onAddMaterial });
    expect(screen.getByText('Models (0)')).toBeInTheDocument();
    expect(screen.getByText('Species materials (1)')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Titanoboa' })).toHaveLength(1);
    await fireEvent.click(screen.getByRole('button', { name: 'Titanoboa' }));
    expect(onSelect).toHaveBeenCalledWith({ kind: 'material', species: 'Titanoboa' });
    await fireEvent.click(screen.getByRole('button', { name: 'Add species materials' }));
    expect(onAddMaterial).toHaveBeenCalled();
  });

  it('also lists the selected species while it has no materials yet', () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModSideList, session, { detail: modDetail(), check: null, selection: { kind: 'material' as const, species: 'Allosaurus' }, onSelect: vi.fn(), onAddSkin: vi.fn() });
    expect(screen.getByRole('button', { name: 'Allosaurus' })).toHaveClass('on');
  });
});

describe('ModSideList vanilla skin materials', () => {
  it('lists vanilla skin materials under Species materials; + Add asks for the form', async () => {
    const onAddVanillaMaterial = vi.fn();
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    const onSelect = vi.fn();
    const detail = modDetail({ vanillaSkinMaterials: [{ species: 'Titanoboa', skin: 'Green', materialsJson: '{"Titanoboa":{"settings":{"_Smoothness":0.5}}}' }] });
    renderWith(ModSideList, session, { detail, check: null, selection: { kind: 'details' as const }, onSelect, onAddSkin: vi.fn(), onAddVanillaMaterial });
    expect(screen.getByText('Vanilla skin materials (1)')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Titanoboa / Green' }));
    expect(onSelect).toHaveBeenCalledWith({ kind: 'vanillaMaterial', species: 'Titanoboa', skin: 'Green' });
    await fireEvent.click(screen.getByRole('button', { name: 'Add vanilla skin materials' }));
    expect(onAddVanillaMaterial).toHaveBeenCalled();
  });
});

describe('ModSideList sizes', () => {
  it('lists the species the mod sizes and selects one; + Add asks for the form', async () => {
    const files = { 'spinosaurus.json': '{"format":1,"target":"Spinosaurus","adult":1.2}' };
    const { props } = setup(modDetail({ sizeFiles: files }));
    expect(screen.getByText('Sizes (1)')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Spinosaurus' }));
    expect(props.onSelect).toHaveBeenCalledWith({ kind: 'size', target: 'Spinosaurus' });
    const onAddSize = vi.fn();
    cleanup();
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModSideList, session, { detail: modDetail(), check: null, selection: { kind: 'details' as const }, onSelect: vi.fn(), onAddSkin: vi.fn(), onAddSize });
    await fireEvent.click(screen.getByRole('button', { name: 'Add sizes' }));
    expect(onAddSize).toHaveBeenCalled();
  });

  it('also lists the selected target while it has no file yet', () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModSideList, session, { detail: modDetail(), check: null, selection: { kind: 'size' as const, target: 'Allosaurus' }, onSelect: vi.fn(), onAddSkin: vi.fn() });
    expect(screen.getByRole('button', { name: 'Allosaurus' })).toBeInTheDocument();
  });
});
