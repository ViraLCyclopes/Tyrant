export interface KeyValueStore {
  get(key: string): string | null;
  set(key: string, value: string): void;
  remove(key: string): void;
}

/** localStorage for per-user conveniences (last workspace, columns). Any access may fail; the app works without it. */
export const browserStore: KeyValueStore = {
  get(key) {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  set(key, value) {
    try {
      localStorage.setItem(key, value);
    } catch {
      // storage unavailable: the convenience is simply not remembered
    }
  },
  remove(key) {
    try {
      localStorage.removeItem(key);
    } catch {
      // storage unavailable
    }
  },
};

export function memoryStore(initial: Record<string, string> = {}): KeyValueStore {
  const values = new Map(Object.entries(initial));
  return {
    get: (key) => values.get(key) ?? null,
    set: (key, value) => void values.set(key, value),
    remove: (key) => void values.delete(key),
  };
}
