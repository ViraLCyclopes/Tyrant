import type { ModTextureDto } from '$lib/rpc/types.gen';

const MB = 1024 * 1024;

/** In-game memory as people read it: "11 MB", "2.7 MB", "0.04 MB". */
export function formatBytes(bytes: number): string {
  const mb = bytes / MB;
  if (mb >= 10) return `${Math.round(mb)} MB`;
  if (mb >= 0.1) return `${Math.round(mb * 10) / 10} MB`;
  return `${Math.round(mb * 100) / 100} MB`;
}

export function describe(t: ModTextureDto): string {
  return `${t.format} · ${formatBytes(t.bytes)} in game`;
}
