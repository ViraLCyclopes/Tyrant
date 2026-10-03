import { visibleRecords, type LogLevel, type LogRecord } from './log';

export interface LogEntry {
  level: LogLevel;
  message: string;
  detail?: string;
  tab: string | null;
}

/** Every log record of this app session, in memory. studio.log stays the full record on disk. */
export class LogStore {
  records = $state<LogRecord[]>([]);
  private seq = 0;
  private readonly listeners = new Set<(record: LogRecord) => void>();

  constructor(
    private readonly perTab = 2000,
    private readonly now: () => number = () => Date.now(),
  ) {}

  add(entry: LogEntry): LogRecord {
    const record: LogRecord = { ...entry, seq: ++this.seq, time: this.now() };
    this.records.push(record);
    const own = this.records.filter((r) => r.tab === record.tab);
    if (own.length > this.perTab) {
      const drop = new Set(own.slice(0, own.length - this.perTab).map((r) => r.seq));
      this.records = this.records.filter((r) => !drop.has(r.seq));
    }
    for (const listener of this.listeners) listener(record);
    return record;
  }

  forTab(tab: string | null): LogRecord[] {
    return visibleRecords(this.records, tab);
  }

  clear(tab: string): void {
    this.records = this.records.filter((r) => r.tab !== tab);
  }

  lastSeq(): number {
    return this.seq;
  }

  onAdd(listener: (record: LogRecord) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
}
