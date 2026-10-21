import type { ModDetail } from '$lib/rpc/types.gen';

/** The species a mod sets sizes for, read from its sizes/ files (a file that cannot be read is skipped; Check reports it). */
export function sizeTargets(detail: ModDetail): string[] {
  const out: string[] = [];
  for (const text of Object.values(detail.sizeFiles ?? {})) {
    try {
      const t = (JSON.parse(text) as { target?: unknown }).target;
      if (typeof t === 'string' && t.trim()) out.push(t.trim());
    } catch {
      // not valid JSON
    }
  }
  return out;
}
