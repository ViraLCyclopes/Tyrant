/** The start screen's pictures: the game's main-menu sauropods, then in-game screenshots (studio/static, see NOTICE). */
export const BACKGROUNDS: readonly string[] = [
  '/hero.jpg',
  ...Array.from({ length: 9 }, (_, i) => `/backgrounds/bg-${String(i + 1).padStart(2, '0')}.jpg`),
];

/** The order to show them in: the first one always first (the sauropods), the rest shuffled. */
export function rotation(images: readonly string[], random: () => number): string[] {
  const rest = images.slice(1);
  for (let i = rest.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [rest[i], rest[j]] = [rest[j], rest[i]];
  }
  return images.length ? [images[0], ...rest] : [];
}
