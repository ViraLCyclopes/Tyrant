import { describe, expect, it } from 'vitest';
import { DIET_ORDER, NICHE_ORDER, dietWarnings, formatFlags, nicheProblems, parseFlags } from './speciesIdentity';

describe('flags text', () => {
  it('formatFlags_keeps_enum_order', () => {
    expect(formatFlags(['Browser', 'Large', 'Grazer', 'NicheSizeAggressive'], NICHE_ORDER)).toBe('Large, NicheSizeAggressive, Grazer, Browser');
    expect(formatFlags(['Kibble', 'Carnivore'], DIET_ORDER)).toBe('Carnivore, Kibble');
  });

  it('parseFlags_handles_blank_and_spaces', () => {
    expect(parseFlags('')).toEqual([]);
    expect(parseFlags('   ')).toEqual([]);
    expect(parseFlags(' Carnivore ,Piscivore,, ')).toEqual(['Carnivore', 'Piscivore']);
  });
});

describe('nicheProblems', () => {
  it('is empty for a size and a feeding role', () => expect(nicheProblems('Adult', ['Large', 'Grazer'])).toEqual([]));
  it('asks for a size', () => expect(nicheProblems('Baby', ['Grazer'])).toEqual(['Baby: pick a size.']));
  it('asks for a feeding role', () => {
    expect(nicheProblems('Adult', ['Large', 'PackHunter'])).toEqual(['Adult: tick at least one of Grazer, Browser, Omnivore, Predator or Scavenger.']);
  });
});

describe('dietWarnings', () => {
  const meat = "Eats meat but has no Predator or Scavenger role, so it will only eat meat from feeders. It won't eat carcasses.";
  const noMeat = 'Has a Predator role but doesn\'t eat meat, so it will hunt but can\'t eat what it kills.';
  const noRule = 'Has a Predator role, but its behaviour has no "Hunts prey" rule, so it won\'t hunt.';
  it('warns about meat without Predator or Scavenger', () => expect(dietWarnings(['Carnivore'], [['Large', 'Grazer']], true)).toEqual([meat]));
  it('warns about Predator without meat', () => expect(dietWarnings(['Herbivore'], [['Large', 'Predator']], true)).toEqual([noMeat]));
  it('warns about Predator without a hunts-prey rule', () => expect(dietWarnings(['Carnivore'], [['Large', 'Predator']], false)).toEqual([noRule]));
  it('is quiet when all fits', () => expect(dietWarnings(['Carnivore'], [['Large', 'Predator']], true)).toEqual([]));
});
