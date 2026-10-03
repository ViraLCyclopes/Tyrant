import type { KeyValueStore } from '$lib/storage';

const key = (type: string) => `tyrant.columns.${type}`;

/** Columns the user chose for a type last time, or null for the default set. */
export function savedColumns(store: KeyValueStore, type: string): string[] | null {
  try {
    const value: unknown = JSON.parse(store.get(key(type)) ?? 'null');
    return Array.isArray(value) && value.every((c) => typeof c === 'string') ? value : null;
  } catch {
    return null;
  }
}

export function saveColumns(store: KeyValueStore, type: string, columns: string[]): void {
  store.set(key(type), JSON.stringify(columns));
}
