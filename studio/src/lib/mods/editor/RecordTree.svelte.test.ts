import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
import RecordTree, { buildRecordTree } from './RecordTree.svelte';

const field = (path: string, value = '1', more: Partial<ModAnimalFieldDto> = {}): ModAnimalFieldDto => ({ path, kind: 'number', value, templateValue: value, editable: true, ...more });
const fields = [
  field('/MinFenceHeight', '3'),
  field('/brain', '{"type":"Brain","name":"AlloBrain"}', { kind: 'reference', editable: false }),
  field('/brain/needs/adult/hitPoints', '660'),
  field('/brain/needs/adult/thirst', '0.5'),
  field('/skinsData', '2', { kind: 'array', editable: false }),
  field('/skinsData/0/name', 'Default', { kind: 'text' }),
  field('/skinsData/1/name', 'Albino', { kind: 'text' }),
  field('/a~1b', 'x', { kind: 'text' }),
];

function setup(list = fields, edits: Record<string, string> = {}, more: { hidden?: (path: string) => boolean; hiddenNote?: string } = {}) {
  const values = $state({ ...Object.fromEntries(list.filter((f) => f.editable).map((f) => [f.path, f.value])), ...edits });
  render(RecordTree, { fields: list, values, ...more });
  return values;
}

describe('Full animal record outline', () => {
  it('folds paths into sections, keeping a section\'s own array or link row on its heading', () => {
    const root = buildRecordTree(fields);
    expect(root.children.map((n) => n.name)).toEqual(['MinFenceHeight', 'brain', 'skinsData', 'a/b']);
    const brain = root.children[1];
    expect(brain.field?.kind).toBe('reference');
    expect(brain.count).toBe(2);
    expect(root.count).toBe(6);
  });

  it('starts with General open and every section folded, opening a section on click', async () => {
    setup();
    expect(screen.getByRole('textbox', { name: '/MinFenceHeight' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^brain 2 fields · link: AlloBrain/ })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('textbox', { name: '/brain/needs/adult/hitPoints' })).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: /^brain / }));
    await fireEvent.click(screen.getByRole('button', { name: /^needs / }));
    await fireEvent.click(screen.getByRole('button', { name: /^adult / }));
    expect(screen.getByRole('textbox', { name: '/brain/needs/adult/hitPoints' })).toHaveValue('660');

    await fireEvent.click(screen.getByRole('button', { name: /^skinsData 2 fields · 2 items/ }));
    expect(screen.getByRole('button', { name: /^#1 / })).toBeInTheDocument();
  });

  it('search shows only the matches with their sections open', async () => {
    setup();
    await fireEvent.input(screen.getByRole('textbox', { name: /Search fields/ }), { target: { value: 'hitpoints' } });
    expect(screen.getByRole('textbox', { name: '/brain/needs/adult/hitPoints' })).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '/brain/needs/adult/thirst' })).toBeNull();
    expect(screen.queryByRole('textbox', { name: '/MinFenceHeight' })).toBeNull();
    expect(screen.getByText('1 match')).toBeInTheDocument();
  });

  it('a broad search shows the first 200 matches and asks to narrow it', async () => {
    setup(Array.from({ length: 250 }, (_, i) => field(`/animationTable/clips/${i}/speed`)));
    await fireEvent.input(screen.getByRole('textbox', { name: /Search fields/ }), { target: { value: 'speed' } });
    expect(screen.getByText(/Showing 200 of 250 matches/)).toBeInTheDocument();
    expect(screen.getAllByRole('textbox', { name: /speed$/ })).toHaveLength(200);
  });

  it('Only changed lists edited fields, and sections count their changes', async () => {
    setup(fields, { '/brain/needs/adult/thirst': '0.9' });
    expect(screen.getByRole('button', { name: /^brain .*· 1 changed/ })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('checkbox', { name: /Only changed/ }));
    expect(screen.getByRole('textbox', { name: '/brain/needs/adult/thirst' })).toHaveValue('0.9');
    expect(screen.queryByRole('textbox', { name: '/brain/needs/adult/hitPoints' })).toBeNull();
    expect(screen.getByText('1 match')).toBeInTheDocument();
  });

  it('editing a field counts it as changed straight away', async () => {
    const values = setup();
    expect(screen.queryByRole('button', { name: /changed/ })).toBeNull();
    await fireEvent.input(screen.getByRole('textbox', { name: '/MinFenceHeight' }), { target: { value: '5' } });
    expect(values['/MinFenceHeight']).toBe('5');
    expect(screen.getByRole('button', { name: /^General 2 fields · 1 changed/ })).toBeInTheDocument();
  });

  it('RecordTree hides paths and shows the note', async () => {
    setup(fields, {}, { hidden: (path) => path.startsWith('/brain/needs'), hiddenNote: 'Rules are set on the Behaviour card.' });
    expect(screen.getByText('Rules are set on the Behaviour card.')).toBeInTheDocument();
    expect(screen.getByText(/^6 fields\./)).toBeInTheDocument();
    await fireEvent.input(screen.getByRole('textbox', { name: /Search fields/ }), { target: { value: 'hitpoints' } });
    expect(screen.queryByRole('textbox', { name: '/brain/needs/adult/hitPoints' })).toBeNull();
    expect(screen.getByText('0 matches')).toBeInTheDocument();
  });

  it('shows no note when nothing is hidden', () => {
    setup(fields, {}, { hidden: (path) => path.startsWith('/nothing'), hiddenNote: 'Rules are set on the Behaviour card.' });
    expect(screen.queryByText('Rules are set on the Behaviour card.')).toBeNull();
  });
});
