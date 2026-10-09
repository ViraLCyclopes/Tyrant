import { describe, expect, it } from 'vitest';
import type { ModSkinDto } from '$lib/rpc/types.gen';
import { modDetail } from '$lib/test/modFixtures';
import { groupName, groupNames, moveGroup, moveSkin, moveSkinTo, renameGroup, sections, setGroup } from './skinGroups';

const base = modDetail().skins[0];
const skin = (id: string, group: string | null = null): ModSkinDto => ({ ...base, id, key: `red-spot/${id}`, name: id, group });
// As the core saves them: ungrouped first, each group together.
const skins = [skin('a'), skin('b'), skin('c', 'Jurassic Park'), skin('d', 'Jurassic Park'), skin('e', 'Isla Sorna')];
const ids = (order: { id: string; group: string | null }[] | null) => order?.map((p) => `${p.id}:${p.group ?? '-'}`).join(' ');

describe('skinGroups', () => {
  it('sections are the ungrouped skins, then each group in order', () => {
    expect(sections(skins).map((s) => [s.group, s.skins.map((k) => k.id)])).toEqual([
      [null, ['a', 'b']],
      ['Jurassic Park', ['c', 'd']],
      ['Isla Sorna', ['e']],
    ]);
    expect(groupNames(skins)).toEqual(['Jurassic Park', 'Isla Sorna']);
  });

  it('a typed name is trimmed, cut to 64, and joins a group whatever its case', () => {
    expect(groupName(skins, '  jurassic PARK ')).toBe('Jurassic Park');
    expect(groupName(skins, '   ')).toBeNull();
    expect(groupName(skins, 'x'.repeat(80))).toBe('x'.repeat(64));
  });

  it('moves a skin within its section, and not past either end', () => {
    expect(ids(moveSkin(skins, 'd', -1))).toBe('a:- b:- d:Jurassic Park c:Jurassic Park e:Isla Sorna');
    expect(moveSkin(skins, 'c', -1)).toBeNull();
    expect(moveSkin(skins, 'b', 1)).toBeNull();
  });

  it('setGroup puts skins at the end of a group, or at the end of the ungrouped ones, ordered as the core saves', () => {
    expect(ids(setGroup(skins, ['a'], 'jurassic park'))).toBe('b:- c:Jurassic Park d:Jurassic Park a:Jurassic Park e:Isla Sorna');
    expect(ids(setGroup(skins, ['e'], null))).toBe('a:- b:- e:- c:Jurassic Park d:Jurassic Park');
  });

  it('renames a group, or merges it into an existing one', () => {
    expect(ids(renameGroup(skins, 'Isla Sorna', 'Site B'))).toBe('a:- b:- c:Jurassic Park d:Jurassic Park e:Site B');
    expect(ids(renameGroup(skins, 'Isla Sorna', 'JURASSIC park'))).toBe('a:- b:- c:Jurassic Park d:Jurassic Park e:Jurassic Park');
  });

  it('moveSkinTo drops a skin before the skin at that place in a section, or at its end', () => {
    expect(ids(moveSkinTo(skins, 'b', null, 0))).toBe('b:- a:- c:Jurassic Park d:Jurassic Park e:Isla Sorna');
    expect(ids(moveSkinTo(skins, 'c', 'Jurassic Park', 2))).toBe('a:- b:- d:Jurassic Park c:Jurassic Park e:Isla Sorna');
    expect(ids(moveSkinTo(skins, 'a', 'Jurassic Park', 1))).toBe('b:- c:Jurassic Park a:Jurassic Park d:Jurassic Park e:Isla Sorna');
    expect(ids(moveSkinTo(skins, 'e', null, 2))).toBe('a:- b:- e:- c:Jurassic Park d:Jurassic Park');
    expect(ids(moveSkinTo(skins, 'd', 'Isla Sorna', 1))).toBe('a:- b:- c:Jurassic Park e:Isla Sorna d:Isla Sorna');
  });

  it('moveSkinTo is null when the drop changes nothing, or names a group or skin that is not there', () => {
    expect(moveSkinTo(skins, 'a', null, 0)).toBeNull();
    expect(moveSkinTo(skins, 'a', null, 1)).toBeNull(); // just after itself
    expect(moveSkinTo(skins, 'd', 'Jurassic Park', 2)).toBeNull();
    expect(moveSkinTo(skins, 'a', 'Site B', 0)).toBeNull();
    expect(moveSkinTo(skins, 'nope', null, 0)).toBeNull();
  });

  it('moveSkinTo matches the group whatever its case, and the group it emptied goes away', () => {
    const order = moveSkinTo(skins, 'e', 'jurassic park', 0);
    expect(ids(order)).toBe('a:- b:- e:Jurassic Park c:Jurassic Park d:Jurassic Park');
    expect(order?.some((p) => p.group === 'Isla Sorna')).toBe(false);
  });

  it('moves a group past another, never above the ungrouped skins', () => {
    expect(ids(moveGroup(skins, 'Isla Sorna', -1))).toBe('a:- b:- e:Isla Sorna c:Jurassic Park d:Jurassic Park');
    expect(moveGroup(skins, 'Jurassic Park', -1)).toBeNull();
    expect(moveGroup(skins, 'Isla Sorna', 1)).toBeNull();
  });
});
