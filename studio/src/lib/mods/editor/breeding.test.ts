import { describe, expect, it } from 'vitest';
import { breedingIssues } from './breeding';

const ok = { months: ['March'], system: 'Polygamous', parenting: 'MotherOnly', gestation: '6', ideal: '2', largest: '4', mostEggs: '0' };

describe('breeding issues', () => {
  it('clean_values_have_no_issues', () => expect(breedingIssues(ok)).toEqual({ errors: [], warnings: [] }));

  it('non_whole_or_negative_clutch_is_an_error', () => {
    expect(breedingIssues({ ...ok, ideal: '2.5' }).errors).toHaveLength(1);
    expect(breedingIssues({ ...ok, largest: '-1' }).errors).toHaveLength(1);
    expect(breedingIssues({ ...ok, mostEggs: '' }).errors).toHaveLength(1);
  });

  it('most_eggs_above_16_is_an_error', () => expect(breedingIssues({ ...ok, mostEggs: '17' }).errors[0]).toMatch(/16/));

  it('warnings_no_months_ideal_over_largest_gestation_zero_father_polygynous', () => {
    expect(breedingIssues({ ...ok, months: [] }).warnings[0]).toMatch(/never breeds/);
    expect(breedingIssues({ ...ok, ideal: '5', largest: '4' }).warnings).toHaveLength(1);
    expect(breedingIssues({ ...ok, gestation: '0' }).warnings).toHaveLength(1);
    expect(breedingIssues({ ...ok, parenting: 'FatherOnly', system: 'Polygynous' }).warnings[0]).toMatch(/no parent/i);
  });

  it('absent_rows_are_skipped', () =>
    expect(breedingIssues({ months: null, system: null, parenting: null, gestation: null, ideal: null, largest: null, mostEggs: null })).toEqual({ errors: [], warnings: [] }));
});
