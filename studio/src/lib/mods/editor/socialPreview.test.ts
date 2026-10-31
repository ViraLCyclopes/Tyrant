import { describe, expect, it } from 'vitest';
import { cohesionWord, exhibitSize, expectedGroups, groupKind, type SocialNumbers } from './socialPreview';

const n: SocialNumbers = { malesMin: 1, malesMax: 2, femalesMin: 2, femalesMax: 4, spaceMale: 1000, spaceFemale: 500, overlap: 0.5, cohesion: 0.5 };

describe('social preview', () => {
  it('exhibit_size_matches_game_formula', () => {
    expect(exhibitSize(n, 2, 4)).toBe(1000 * 1.5 + 500 * 2.5); // lerp(2,1,.5)=1.5, lerp(4,1,.5)=2.5
    expect(exhibitSize(n, 0, 4)).toBe(500 * 2.5); // 0 males: no male part
    expect(exhibitSize(n, 0, 0)).toBe(0);
  });

  it('expected_groups_matches_game_loop', () => {
    expect(expectedGroups(n, 2, 4)).toEqual({ groups: 1, nomads: 0 });
    expect(expectedGroups(n, 5, 9)).toEqual({ groups: 2, nomads: 2 }); // (5,9)→(3,5)→(1,1); 1 female < 2 fewest stops it
    expect(expectedGroups({ ...n, malesMax: 0, femalesMax: 0 }, 3, 2)).toEqual({ groups: 0, nomads: 5 });
  });

  it('cohesion_word_buckets_and_none_without_herd', () => {
    expect(cohesionWord({ ...n, cohesion: 0.33 })).toBe('Low');
    expect(cohesionWord({ ...n, cohesion: 0.67 })).toBe('Medium');
    expect(cohesionWord({ ...n, cohesion: 0.9 })).toBe('High');
    expect(cohesionWord({ ...n, malesMax: 1, femalesMax: 0 })).toBe('None');
  });

  it('group_kind_follows_game_order', () => {
    expect(groupKind({ ...n, malesMin: 0, malesMax: 0, femalesMin: 0, femalesMax: 0 })).toBe('none');
    expect(groupKind({ ...n, malesMin: 1, malesMax: 1, femalesMin: 1, femalesMax: 1 })).toBe('couples');
    expect(groupKind({ ...n, malesMax: 0 })).toBe('females');
    expect(groupKind({ ...n, femalesMax: 0 })).toBe('males');
    expect(groupKind(n)).toBe('mixed');
  });
});
