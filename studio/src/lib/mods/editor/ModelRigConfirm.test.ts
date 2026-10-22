import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModModelRigPreviewDto } from '$lib/rpc/types.gen';
import { FakeRpc } from '$lib/test/fakeRpc';
import ModelRigConfirm from './ModelRigConfirm.svelte';
import { chooseModelRig } from './modelRig';

const bone = (name: string) => ({ bone: name, move: [0, 0.5, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] });
const preview = (over: Partial<ModModelRigPreviewDto> = {}): ModModelRigPreviewDto => ({
  source: 'bones', bones: [bone('Jaw'), bone('Tail1')], current: null, question: 'none', changes: true, errors: [], warnings: [], rigFile: null, errorsIfUseFile: [], errorsIfKeep: [], ...over,
});

function show(p: ModModelRigPreviewDto, action: 'Add' | 'Replace' = 'Add') {
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  render(ModelRigConfirm, { preview: p, action, onConfirm, onCancel });
  return { onConfirm, onCancel };
}

describe('ModelRigConfirm', () => {
  it('shows the bones and the source', () => {
    show(preview());
    expect(screen.getByText("From the model's bones")).toBeInTheDocument();
    expect(screen.getByText('2 bones')).toBeInTheDocument();
    expect(screen.getByText('Jaw')).toBeInTheDocument();
    expect(screen.getAllByText('move 0, 0.5, 0')).toHaveLength(2);
  });

  it('names the rig file', () => {
    show(preview({ source: 'rigFile', rigFile: 'D:\\m\\Ultimasaurus.fbx.rig.json' }));
    expect(screen.getByText('From Ultimasaurus.fbx.rig.json')).toBeInTheDocument();
  });

  it("useOrKeep preselects using the model's rig edit", async () => {
    const { onConfirm } = show(preview({ question: 'useOrKeep', current: [bone('A'), bone('B'), bone('C')] }));
    expect(screen.getByRole('radio', { name: "Use the model's rig edit" })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Keep the current one (3 bones)' })).not.toBeChecked();
    await fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith('useFile');
  });

  it('keepOrRemove preselects keep', async () => {
    const { onConfirm } = show(preview({ question: 'keepOrRemove', source: 'gameSkeleton', bones: [], current: [bone('A')] }), 'Replace');
    expect(screen.getByRole('radio', { name: 'Keep the current rig edit' })).toBeChecked();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace' }));
    expect(onConfirm).toHaveBeenCalledWith('keepCurrent');
  });

  it('keepOrRemove lists the bones that keep would preserve', () => {
    show(preview({ question: 'keepOrRemove', source: 'gameSkeleton', bones: [], current: [bone('Wing1')] }));
    expect(screen.getByText('The current rig edit: 1 bone')).toBeInTheDocument();
    expect(screen.getByText('Wing1')).toBeInTheDocument();
  });

  it('errors disable the confirm button', async () => {
    const { onCancel } = show(preview({ errors: ['Bone Foo is not in the skeleton'] }));
    expect(screen.getByRole('alert')).toHaveTextContent('Bone Foo');
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
    await fireEvent.keyDown(screen.getByRole('group', { name: 'Rig edit' }), { key: 'Escape' });
    expect(onCancel).toHaveBeenCalled();
  });

  it("keep stays open when only the model's rig edit breaks a rule", async () => {
    const bad = "Rig edit of Carcharodontosaurus: rig edit bone(s) Wing.L is not in the species' skeleton";
    const { onConfirm } = show(preview({ question: 'useOrKeep', current: [bone('A')], errors: [bad], errorsIfUseFile: [bad], errorsIfKeep: [] }));
    expect(screen.getByRole('alert')).toHaveTextContent('Wing.L');
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();

    await fireEvent.click(screen.getByRole('radio', { name: 'Keep the current one (1 bones)' }));

    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByRole('button', { name: 'Add' })).toBeEnabled();
    await fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith('keepCurrent');
  });

  it('keeping a current rig edit that breaks a rule says so, and removing it stays open', async () => {
    const why = "The rig edit already on Carcharodontosaurus breaks a rule, not the model. Clear it (Clear rig edit on the model page, or 'tyrant mod rig m \"Carcharodontosaurus\" --clear'), then add the model again.";
    const { onConfirm } = show(preview({ question: 'keepOrRemove', source: 'gameSkeleton', bones: [], current: [bone('Wing.L')], errors: [why], errorsIfKeep: [why], errorsIfUseFile: [] }), 'Replace');
    expect(screen.getByRole('alert')).toHaveTextContent('The rig edit already on Carcharodontosaurus breaks a rule');
    expect(screen.getByRole('button', { name: 'Replace' })).toBeDisabled();

    await fireEvent.click(screen.getByRole('radio', { name: 'Remove it' }));

    expect(screen.queryByRole('alert')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace' }));
    expect(onConfirm).toHaveBeenCalledWith('useFile');
  });

  it('folds a long bone list', () => {
    show(preview({ bones: Array.from({ length: 9 }, (_, i) => bone(`B${i}`)) }));
    expect(document.querySelector('details')).not.toBeNull();
  });
});

describe('chooseModelRig', () => {
  const params = { id: 'm', file: 'x.glb' };

  it('re-import with no change goes straight through', async () => {
    const rpc = new FakeRpc().on('mods.readModelRig', () => preview({ changes: false, question: 'useOrKeep' }));
    expect(await chooseModelRig(rpc, params, { reimport: true })).toBe('auto');
  });

  it('an unremarkable model goes straight through', async () => {
    const rpc = new FakeRpc().on('mods.readModelRig', () => preview({ bones: [], source: 'gameSkeleton' }));
    expect(await chooseModelRig(rpc, params, { reimport: false })).toBe('auto');
  });
});
