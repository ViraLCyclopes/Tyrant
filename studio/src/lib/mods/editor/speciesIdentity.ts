/** Plain-word helpers for the game's flags text (e.g. `Carnivore, Piscivore`) behind the Diet and niche card. */

/** The flags in a flags text; blank text has none. */
export const parseFlags = (text: string): string[] =>
  text.split(',').map((s) => s.trim()).filter((s) => s !== '');

/** The flags written back the way the game does: enum order, joined with ', '. Flags not in `order` follow, in their given order. */
export function formatFlags(names: readonly string[], order: readonly string[]): string {
  const known = order.filter((n) => names.includes(n));
  const unknown = names.filter((n) => !order.includes(n));
  return [...known, ...unknown].join(', ');
}

export const DIETS: readonly (readonly [string, string])[] = [['Herbivore', 'Plants'], ['Carnivore', 'Meat'], ['Insectivore', 'Insects'], ['Piscivore', 'Fish'], ['Frugivore', 'Fruit']];
export const DIET_ORDER: readonly string[] = [...DIETS.map(([name]) => name), 'Kibble'];

export const NICHE_SIZES: readonly string[] = ['Tiny', 'Small', 'Medium', 'Large', 'Huge'];
export const NICHE_ROLES: readonly (readonly [string, string])[] = [
  ['Grazer', 'Grazer'], ['Browser', 'Browser'], ['Omnivore', 'Omnivore'], ['Predator', 'Predator'], ['Scavenger', 'Scavenger'],
  ['PackHunter', 'Pack hunter'], ['MacroPredator', 'Hunts bigger prey'], ['NicheSizeAggressive', 'Fights same-size rivals'],
];
export const NICHE_ORDER: readonly string[] = [...NICHE_SIZES, 'NicheSizeAggressive', 'Grazer', 'Browser', 'Omnivore', 'Predator', 'Scavenger', 'PackHunter', 'MacroPredator'];
export const NICHE_AGES: readonly (readonly [string, string])[] = [['/niches/infantNiche', 'Baby'], ['/niches/adolescentNiche', 'Juvenile'], ['/niches/adultNiche', 'Adult']];

const FEEDING_ROLES = ['Grazer', 'Browser', 'Omnivore', 'Predator', 'Scavenger'];
/** Foods that count as meat: the game only lets Carnivores eat carcasses and hunted meat. */
const MEAT = ['Carnivore'];

/** Errors for one age's niche flags: it needs a size and a feeding role. */
export function nicheProblems(label: string, flags: string[]): string[] {
  const found: string[] = [];
  if (!flags.some((f) => NICHE_SIZES.includes(f))) found.push(`${label}: pick a size.`);
  if (!flags.some((f) => FEEDING_ROLES.includes(f))) found.push(`${label}: tick at least one of Grazer, Browser, Omnivore, Predator or Scavenger.`);
  return found;
}

/** The rules that make a species hunt live prey: always, or when starving. */
const HUNTING_RULES = ['Aggression_Predation', 'Aggression_PredationStarving'];
/** Whether a species with these behaviour rules hunts live prey. */
export const huntsPrey = (rules: readonly string[]): boolean => rules.some((r) => HUNTING_RULES.includes(r));

/** Warnings (not errors) from the diet (null when the template has no diet, which skips the diet warnings) with the niches of the ages the template has. */
export function dietWarnings(diet: string[] | null, niches: string[][], hasPredation: boolean): string[] {
  const found: string[] = [];
  const eatsMeat = diet?.some((d) => MEAT.includes(d)) ?? false;
  const predator = niches.some((n) => n.includes('Predator'));
  if (diet && eatsMeat && niches.length && !niches.some((n) => n.includes('Predator') || n.includes('Scavenger')))
    found.push("Eats meat but has no Predator or Scavenger role, so it will only eat meat from feeders. It won't eat carcasses.");
  if (diet && predator && !eatsMeat) found.push("Has a Predator role but doesn't eat meat, so it will hunt but can't eat what it kills.");
  if (predator && !hasPredation) found.push('Has a Predator role, but its behaviour has no "Hunts prey" rule, so it won\'t hunt.');
  return found;
}
