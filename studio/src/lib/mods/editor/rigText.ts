import type { ModRigBone } from '$lib/rpc/types.gen';

type Parts = Pick<ModRigBone, 'move' | 'rotate' | 'scale'>;

function round(v: number) {
  return Math.round(v * 10000) / 10000;
}

/** Degrees about X, Y and Z, for reading (the rig edit keeps the quaternion). */
function degrees([x, y, z, w]: number[]) {
  const rx = Math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
  const ry = Math.asin(Math.max(-1, Math.min(1, 2 * (w * y - z * x))));
  const rz = Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
  return [rx, ry, rz].map((r) => round((r * 180) / Math.PI));
}

/** What a rig edit does to one bone, in plain words: "move 0, 0.1, 0", "rotate 0, 15, 0°", "scale 1.2, 1.2, 1.2". */
export function boneParts(b: Parts): string[] {
  const out: string[] = [];
  if (b.move.some((v) => Math.abs(v) > 1e-6)) out.push(`move ${b.move.map(round).join(', ')}`);
  if (Math.abs(b.rotate[3]) < 1 - 1e-7) out.push(`rotate ${degrees(b.rotate).join(', ')}°`);
  if (b.scale.some((v) => Math.abs(v - 1) > 1e-6)) out.push(`scale ${b.scale.map(round).join(', ')}`);
  return out;
}
