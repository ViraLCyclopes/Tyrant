/** Option lists and checks for the Breeding card: the game's enum names, with plain labels. */

export type Option = readonly [value: string, label: string];

export const MONTHS: readonly string[] = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export const MATING_SYSTEMS: readonly (readonly [value: string, label: string, description: string])[] = [
  ['Monogamous', 'Monogamous', 'One male and one female pair up.'],
  ['Polygamous', 'Polygamous', 'Animals can have more than one mate.'],
  ['Polyandrous', 'Polyandrous', 'A female mates with several males.'],
  ['Polygynous', 'Polygynous', 'A male mates with several females.'],
  ['Promiscuous', 'Promiscuous', 'Anyone mates with anyone, with no lasting pairs.'],
];

/** Fertility levels with the game's chance as a percent of average. */
export const FECUNDITY: readonly Option[] = [
  ['Lowest', 'Lowest (10%)'], ['VeryLow', 'Very low (30%)'], ['Low', 'Low (50%)'], ['MediumLow', 'Medium low (75%)'], ['Average', 'Average (100%)'],
  ['MediumHigh', 'Medium high (125%)'], ['High', 'High (150%)'], ['VeryHigh', 'Very high (180%)'], ['Highest', 'Highest (220%)'],
];

export const PARENTING: readonly Option[] = [['MotherOnly', 'Mother only'], ['FatherOnly', 'Father only'], ['BothParents', 'Both parents'], ['NoParents', 'Neither']];
export const DIFFICULTY: readonly Option[] = ['Easy', 'Medium', 'Hard'].map((n) => [n, n] as const);
export const SIT_ON_EGGS: readonly Option[] = ['Never', 'Sometimes', 'Regularly', 'Often', 'Always'].map((n) => [n, n] as const);

export const BREEDING_PATHS = {
  months: '/brain/mating/shared/matingSeason',
  system: '/brain/mating/shared/matingSystem',
  fecundity: '/brain/mating/shared/fecundityLevel',
  gestation: '/brain/reproduction/shared/gestationTimeMonths',
  ideal: '/brain/reproduction/shared/idealClutchSize',
  largest: '/brain/reproduction/shared/maxClutchSize',
  mostEggs: '/brain/reproduction/shared/maxEmbryoCountOverride',
  parenting: '/brain/reproduction/adult/parentingMethod',
  familyCohesion: '/brain/social/infant/familyPositionalCohesion',
  difficulty: '/brain/reproduction/shared/embryoSurvivalDifficulty',
  sitOnEggs: '/brain/reproduction/adult/sitOnEggsFrequency',
} as const;

export type BreedingValues = {
  months: string[] | null; system: string | null; parenting: string | null;
  gestation: string | null; ideal: string | null; largest: string | null; mostEggs: string | null; familyCohesion: string | null;
};

const whole = (text: string) => /^\d+$/.test(text.trim());
const num = (text: string | null) => (text === null || text.trim() === '' || !Number.isFinite(Number(text)) ? null : Number(text));

/** Errors (the game can't load the value, so Save is refused) and warnings. A null value means its row isn't in the record. */
export function breedingIssues(v: BreedingValues): { errors: string[]; warnings: string[] } {
  const errors: string[] = [];
  const warnings: string[] = [];
  const counts: [string | null, string][] = [[v.ideal, 'Ideal clutch'], [v.largest, 'Largest clutch'], [v.mostEggs, 'Most eggs laid']];
  for (const [text, label] of counts) if (text !== null && !whole(text)) errors.push(`${label} must be a whole number of 0 or more.`);
  const decimals: [string | null, string][] = [[v.gestation, 'Gestation'], [v.familyCohesion, 'Babies stay close to family']];
  for (const [text, label] of decimals) if (text !== null && num(text) === null) errors.push(`${label} needs a number.`);
  if (v.mostEggs !== null && whole(v.mostEggs) && Number(v.mostEggs) > 16)
    errors.push('Most eggs laid can be at most 16 (the game keeps it in a byte and its own editor stops at 16).');

  if (v.months !== null && v.months.length === 0) warnings.push('No breeding month is ticked, so the species never breeds.');
  const ideal = num(v.ideal);
  const largest = num(v.largest);
  if (ideal !== null && largest !== null && ideal > largest) warnings.push(`Ideal clutch (${ideal}) is larger than the largest clutch (${largest}).`);
  const gestation = num(v.gestation);
  if (gestation !== null && gestation <= 0) warnings.push('Gestation is 0 months or less.');
  if (v.parenting === 'FatherOnly' && v.system === 'Polygynous')
    warnings.push('Father only with Polygynous mating: no parent looks after the babies.');
  return { errors, warnings };
}
