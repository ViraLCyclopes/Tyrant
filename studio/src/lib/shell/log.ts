export type LogLevel = 'info' | 'warn' | 'error';

/** One log line. `tab: null` is a core line, shown in every tab (Scute's rule). */
export interface LogRecord {
  seq: number;
  time: number;
  level: LogLevel;
  message: string;
  detail?: string;
  tab: string | null;
}

export function toLevel(level: string): LogLevel {
  return level === 'warn' || level === 'error' ? level : 'info';
}

/** What a tab's panel shows: its own records plus every core one. Ported from Scute's shell/log.ts. */
export function visibleRecords(all: LogRecord[], tab: string | null): LogRecord[] {
  return all.filter((r) => r.tab === null || r.tab === tab);
}

/** Which tabs deserve a marker, and how loud. Core records never mark a tab. Ported from Scute's shell/log.ts. */
export function attentionFor(all: LogRecord[]): Map<string, 'warn' | 'error'> {
  const out = new Map<string, 'warn' | 'error'>();
  for (const record of all) {
    if (record.tab === null) continue;
    if (record.level === 'error') out.set(record.tab, 'error');
    else if (record.level === 'warn' && out.get(record.tab) !== 'error') out.set(record.tab, 'warn');
  }
  return out;
}

/**
 * The log panel's size (width at the side, height at the bottom) while its edge is dragged from `start` to `now`
 * (pointer positions). It never covers more than 60 % of the window, nor shrinks below 80.
 */
export function panelSize(drag: { from: number; start: number; now: number; position: 'bottom' | 'side'; viewport: number }): number {
  const wanted = drag.from + (drag.start - drag.now);
  return Math.round(Math.min(drag.viewport * 0.6, Math.max(80, wanted)));
}
