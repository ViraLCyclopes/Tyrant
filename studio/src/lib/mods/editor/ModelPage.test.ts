import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ModDetail, ModModelDto, ModSkinDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import ModelPage from './ModelPage.svelte';

const viewer = vi.hoisted(() => ({
  showModels: vi.fn(), setColors: vi.fn(), setAnimalMaps: vi.fn(), setSkeleton: vi.fn(), setTextures: vi.fn(), setSkin: vi.fn(),
  setGround: vi.fn(), setSky: vi.fn(), frame: vi.fn(), dispose: vi.fn(), pause: vi.fn(), resume: vi.fn(),
}));
vi.mock('$lib/assets/viewer', () => ({ showModels: viewer.showModels }));

const model: ModModelDto = {
  target: 'Carcharodontosaurus', skin: null, file: 'models/carch-1a2b.glb', stale: false, origin: null, originChanged: false, errors: [],
  warnings: ["The rest pose of bone 'Jaw' differs from the game's"],
  lods: [{ file: 'a.lod0.tmesh', vertices: 9000, index32: false, vanilla: 20000 }, { file: 'a.lod1.tmesh', vertices: 3000, index32: false, vanilla: 7000 }],
};

function setup() {
  const rpc = new FakeRpc().on('mods.readModelRig', () => ({ source: 'gameSkeleton', bones: [], current: null, question: 'none', changes: false, errors: [], warnings: [], rigFile: null, errorsIfUseFile: [], errorsIfKeep: [] })).on('mods.modelPreview', () => ({
    prefabRef: 'carch#1', materials: [],
    lods: [{ file: 'D:\\p\\lod0.glb', vertices: 9000 }, { file: 'D:\\p\\lod1.glb', vertices: 3000 }],
  }));
  const { showModels, ...methods } = viewer;
  showModels.mockResolvedValue({ ...methods, failures: [] });
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const doc = { id: 'big-carch', detail: { revision: 'r1', models: [model] } as unknown as ModDetail, edit: vi.fn(async () => true) };
  return { rpc, session, doc, platform };
}

beforeEach(() => Object.values(viewer).forEach((fn) => fn.mockReset()));

describe('ModelPage', () => {
  it('previews LOD 0 and switches to another LOD', async () => {
    const { session, doc } = setup();
    renderWith(ModelPage, session, { doc, model });

    await waitFor(() => expect(viewer.showModels.mock.calls[0]?.[1]).toEqual([{ file: 'D:\\p\\lod0.glb', url: 'asset://D:\\p\\lod0.glb' }]));
    await fireEvent.click(screen.getByRole('button', { name: 'LOD 1' }));
    await waitFor(() => expect(viewer.showModels.mock.calls.at(-1)![1]).toEqual([{ file: 'D:\\p\\lod1.glb', url: 'asset://D:\\p\\lod1.glb' }]));
  });

  it('switching LODs reuses the preview it already has', async () => {
    const { rpc, session, doc } = setup();
    renderWith(ModelPage, session, { doc, model });
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalledTimes(1));

    await fireEvent.click(screen.getByRole('button', { name: 'LOD 1' }));
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalledTimes(2));
    await fireEvent.click(screen.getByRole('button', { name: 'LOD 0' }));

    await waitFor(() => expect(viewer.showModels).toHaveBeenCalledTimes(3));
    expect(rpc.callsTo('mods.modelPreview')).toHaveLength(1);
  });

  it('a source file changed since it was added can be imported again', async () => {
    const { session, doc } = setup();
    renderWith(ModelPage, session, { doc, model: { ...model, origin: 'D:\\blender\\carch.glb', originChanged: true } });

    expect(screen.getByText(/D:\\blender\\carch.glb changed since you added it/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Re-import' }));

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\carch.glb', target: 'Carcharodontosaurus', skin: null, rig: 'auto' }));
  });

  it('shows each LOD against the game and the warnings', () => {
    const { session, doc } = setup();
    renderWith(ModelPage, session, { doc, model });

    expect(screen.getByText('LOD 0: 9,000 vertices (game: 20,000)')).toBeInTheDocument();
    expect(screen.getByText(/rest pose of bone 'Jaw'/)).toBeInTheDocument();
  });

  it('Rebuild LODs, Replace… and Remove go through the undoable edits', async () => {
    const { session, doc, platform } = setup();
    platform.files.push('D:\\blender\\carch2.glb');
    const onRemoved = vi.fn();
    renderWith(ModelPage, session, { doc, model, onRemoved });

    await fireEvent.click(screen.getByRole('button', { name: 'Rebuild LODs' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Replace…' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(onRemoved).toHaveBeenCalled());
    expect(doc.edit).toHaveBeenCalledWith('mods.rebuildModels', {});
    expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\carch2.glb', target: 'Carcharodontosaurus', skin: null, rig: 'auto' });
    expect(doc.edit).toHaveBeenCalledWith('mods.removeModel', { target: 'Carcharodontosaurus', skin: null });
  });

  it('opening another model with the rig question open saves nothing, and the other model shows no question', async () => {
    const { rpc, session, doc, platform } = setup();
    rpc.on('mods.readModelRig', () => ({
      source: 'bones', bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }], current: [], question: 'useOrKeep', changes: true,
      errors: [], warnings: [], rigFile: null, errorsIfUseFile: [], errorsIfKeep: [],
    }));
    platform.files.push('D:\\blender\\carch2.glb');
    const view = renderWith(ModelPage, session, { doc, model });
    await fireEvent.click(screen.getByRole('button', { name: 'Replace…' }));
    await screen.findByRole('group', { name: 'Rig edit' });

    await view.rerender({ doc, model: { ...model, target: 'Allosaurus Anax', file: 'models/allo-1a2b.glb' } });

    await waitFor(() => expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull());
    expect(doc.edit).not.toHaveBeenCalled();
  });

  it('a fresh copy of the same model keeps the rig question open', async () => {
    const { rpc, session, doc, platform } = setup();
    rpc.on('mods.readModelRig', () => ({
      source: 'bones', bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }], current: [], question: 'useOrKeep', changes: true,
      errors: [], warnings: [], rigFile: null, errorsIfUseFile: [], errorsIfKeep: [],
    }));
    platform.files.push('D:\\blender\\carch2.glb');
    const view = renderWith(ModelPage, session, { doc, model });
    await fireEvent.click(screen.getByRole('button', { name: 'Replace…' }));
    await screen.findByRole('group', { name: 'Rig edit' });

    await view.rerender({ doc, model: { ...model } }); // as the editor reading the saved mod again does

    expect(screen.getByRole('group', { name: 'Rig edit' })).toBeInTheDocument();
    await fireEvent.click(screen.getAllByRole('button', { name: 'Replace' }).at(-1)!);
    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\carch2.glb', target: 'Carcharodontosaurus', skin: null, rig: 'useFile' }));
  });

  it('a model that cannot be previewed says why', async () => {
    const { rpc, session, doc } = setup();
    rpc.on('mods.modelPreview', () => {
      throw new Error('The model has no built levels of detail to show');
    });
    renderWith(ModelPage, session, { doc, model });

    expect(await screen.findByText(/no built levels of detail/)).toBeInTheDocument();
    expect(viewer.showModels).not.toHaveBeenCalled();
  });

  it('shows a rig edit without a model of its own as such, with no preview or LODs', async () => {
    const { session, doc, rpc } = setup();
    rpc.on('mods.rig', () => ({ bones: [{ bone: 'Jaw', move: [0, 0.1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }], hasModel: false, errors: [], warnings: [] }));
    const rigOnly: ModModelDto = { ...model, file: '', lods: [], warnings: [], rig: [{ bone: 'Jaw', move: [0, 0.1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }] };
    renderWith(ModelPage, session, { doc, model: rigOnly });

    expect(await screen.findByText(/Rig edit only/)).toBeInTheDocument();
    expect(await screen.findByText('Jaw')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rebuild LODs' })).toBeNull();
    expect(rpc.callsTo('mods.modelPreview')).toHaveLength(0);
  });
});

describe('ModelPage skin picker', () => {
  let n = 0;
  const skinOf = (id: string, extra: Partial<ModSkinDto> = {}) =>
    ({ id, key: `m/${id}`, species: 'Carcharodontosaurus', name: id.toUpperCase(), base: '0', ...extra }) as unknown as ModSkinDto;
  function withSkins(skins: ModSkinDto[]) {
    const ctx = setup();
    ctx.rpc
      .on('mods.skinModel', () => ({ prefabRef: 'carch#1', maps: { diffuse: 'D:/m/male_D.png' } }))
      .on('assets.preview', () => ({ kind: 'model', files: ['D:/c/Carch.glb'], materials: [], skins: null }));
    ctx.doc.id = `picker-mod-${++n}`; // the choice is remembered per mod
    ctx.doc.detail = { revision: 'r1', models: [model], skins } as unknown as ModDetail;
    return ctx;
  }

  it('defaults to the starting skin', async () => {
    const { rpc, session, doc } = withSkins([skinOf('a'), skinOf('b', { starting: true })]);
    renderWith(ModelPage, session, { doc, model });

    expect(await screen.findByRole('combobox', { name: 'Skin' })).toHaveValue('b');
    await waitFor(() => expect(rpc.callsTo('mods.skinModel').at(-1)?.params).toMatchObject({ skin: 'b' }));
    expect(rpc.callsTo('mods.modelPreview')).toHaveLength(0);
  });

  it('defaults to the first skin when there is no starting skin', async () => {
    const { rpc, session, doc } = withSkins([skinOf('a'), skinOf('b')]);
    renderWith(ModelPage, session, { doc, model });

    expect(await screen.findByRole('combobox', { name: 'Skin' })).toHaveValue('a');
    await waitFor(() => expect(rpc.callsTo('mods.skinModel').at(-1)?.params).toMatchObject({ skin: 'a' }));
  });

  it('lists only skins that wear this model', async () => {
    const { session, doc } = withSkins([skinOf('a'), skinOf('other', { species: 'Allosaurus' }), skinOf('own', { model: 'models/own.glb' }), skinOf('b')]);
    renderWith(ModelPage, session, { doc, model });

    const options = within(await screen.findByRole('combobox', { name: 'Skin' })).getAllByRole('option').map((o) => o.textContent);
    expect(options).toEqual(['A', 'B', "Game's materials"]);
    expect(screen.getByRole('combobox', { name: 'Skin' })).toHaveAccessibleDescription(/maps a skin does not have come from the skin it is based on/);
  });

  it("a skin's own model page lists that skin only", async () => {
    const { session, doc } = withSkins([skinOf('a'), skinOf('own', { model: 'models/own.glb' })]);
    renderWith(ModelPage, session, { doc, model: { ...model, skin: 'own' } });

    expect(within(await screen.findByRole('combobox', { name: 'Skin' })).getAllByRole('option').map((o) => o.textContent)).toEqual(['OWN', "Game's materials"]);
  });

  it("no skins shows game's materials without a picker", async () => {
    const { session, doc } = withSkins([]);
    renderWith(ModelPage, session, { doc, model });

    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());
    expect(screen.queryByRole('combobox', { name: 'Skin' })).toBeNull();
  });

  it("choosing Game's materials shows the template view", async () => {
    const { rpc, session, doc } = withSkins([skinOf('a')]);
    renderWith(ModelPage, session, { doc, model });
    await waitFor(() => expect(rpc.callsTo('mods.skinModel')).not.toHaveLength(0));

    await fireEvent.change(screen.getByRole('combobox', { name: 'Skin' }), { target: { value: '' } });

    await waitFor(() => expect(viewer.showModels.mock.calls.at(-1)?.[1]).toEqual([{ file: 'D:\\p\\lod0.glb', url: 'asset://D:\\p\\lod0.glb' }]));
    expect(screen.getByRole('combobox', { name: 'Skin' })).toHaveValue('');
  });

  it('remembers the choice for the model while the app is open', async () => {
    const { session, doc } = withSkins([skinOf('a'), skinOf('b')]);
    const first = renderWith(ModelPage, session, { doc, model });
    await fireEvent.change(await screen.findByRole('combobox', { name: 'Skin' }), { target: { value: 'b' } });
    first.unmount();

    renderWith(ModelPage, session, { doc, model });
    expect(await screen.findByRole('combobox', { name: 'Skin' })).toHaveValue('b');
  });

  it('passes the chosen LOD and strict to skinModel', async () => {
    const { rpc, session, doc } = withSkins([skinOf('a')]);
    renderWith(ModelPage, session, { doc, model });
    await waitFor(() => expect(rpc.callsTo('mods.skinModel').at(-1)?.params).toMatchObject({ skin: 'a', lod: 0, strict: true }));

    await fireEvent.click(screen.getByRole('button', { name: 'LOD 1' }));

    await waitFor(() => expect(rpc.callsTo('mods.skinModel').at(-1)?.params).toMatchObject({ skin: 'a', lod: 1, strict: true }));
  });

  it('shows the reason when the skin view cannot be built', async () => {
    const { rpc, session, doc } = withSkins([skinOf('a')]);
    rpc.on('mods.skinModel', () => {
      throw new Error('The model is not built yet: Check builds it.');
    });
    renderWith(ModelPage, session, { doc, model });

    expect(await screen.findByText(/not built yet/)).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Skin' })).toBeEnabled();
  });
});
