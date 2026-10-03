import { afterEach, describe, expect, it, vi } from 'vitest';
import { debounce } from './debounce';

describe('debounce', () => {
  afterEach(() => vi.useRealTimers());

  it('runs once, after the calls stop', () => {
    vi.useFakeTimers();
    const fn = vi.fn();
    const later = debounce(fn, 250);

    later();
    vi.advanceTimersByTime(200);
    later();
    vi.advanceTimersByTime(200);
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(60);
    expect(fn).toHaveBeenCalledTimes(1);
  });
});
