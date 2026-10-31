/** Tyrant's own copy of the numbers the game's animal panel works out from a species' social settings. */

export type SocialNumbers = {
  malesMin: number; malesMax: number; femalesMin: number; femalesMax: number;
  spaceMale: number; spaceFemale: number; overlap: number; cohesion: number;
};

const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

/** Expected exhibit size (m²) for this many males and females: each sex present needs its space, shrunk by the allowed crowding. */
export function exhibitSize(n: SocialNumbers, males: number, females: number): number {
  const m = males > 0 ? lerp(males, 1, n.overlap) : 0;
  const f = females > 0 ? lerp(females, 1, n.overlap) : 0;
  return n.spaceMale * m + n.spaceFemale * f;
}

/** How many groups this many animals form, and how many are left on their own, the way the game counts them. */
export function expectedGroups(n: SocialNumbers, males: number, females: number): { groups: number; nomads: number } {
  if (n.malesMax + n.femalesMax === 0) return { groups: 0, nomads: males + females };
  let m = males;
  let f = females;
  let groups = 0;
  // Full groups first, while both sexes can fill a largest group.
  for (; groups < 9999 && m >= n.malesMax && f >= n.femalesMax; groups++) {
    m -= n.malesMax;
    f -= n.femalesMax;
  }
  // Then smaller groups, while each sex still has its fewest and not everyone is down to one animal.
  for (; groups < 9999; groups++) {
    if (m < n.malesMin || f < n.femalesMin) break;
    if (m <= 1 && f <= 1) break;
    m -= Math.min(Math.max(m, n.malesMin), n.malesMax);
    f -= Math.min(Math.max(f, n.femalesMin), n.femalesMax);
  }
  return { groups, nomads: Math.max(0, m) + Math.max(0, f) };
}

/** The species wants to live in a herd: its largest group has more than one animal. */
export const desiresHerd = (n: SocialNumbers): boolean => n.malesMax + n.femalesMax > 1;

/** The game's three cohesion levels (at most 0.33, at most 0.67, higher), or None without a herd. */
export function cohesionWord(n: SocialNumbers): 'None' | 'Low' | 'Medium' | 'High' {
  if (!desiresHerd(n)) return 'None';
  return n.cohesion <= 0.33 ? 'Low' : n.cohesion <= 0.67 ? 'Medium' : 'High';
}

/** Which kind of sentence the game shows about the species' groups. */
export function groupKind(n: SocialNumbers): 'none' | 'couples' | 'females' | 'males' | 'mixed' {
  const all = [n.malesMin, n.femalesMin, n.malesMax, n.femalesMax];
  if (Math.max(...all) <= 0) return 'none';
  if (all.every((x) => x === 1)) return 'couples';
  if (n.malesMax === 0) return 'females';
  if (n.femalesMax === 0) return 'males';
  return 'mixed';
}
