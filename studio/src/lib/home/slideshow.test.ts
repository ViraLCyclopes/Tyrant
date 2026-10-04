import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Slideshow } from './slideshow.svelte';

const images = ['/hero.jpg', '/a.jpg', '/b.jpg', '/c.jpg'];
const shown = (s: Slideshow) => s.layers[s.front];

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('Slideshow', () => {
  it('starts on the first picture with the second waiting behind it', () => {
    const s = new Slideshow(images, 500);

    expect(shown(s)).toBe('/hero.jpg');
    expect(s.layers[1 - s.front]).toBe('/a.jpg');
  });

  it('next shows the following picture and loads the one after it behind, once the fade is over', () => {
    const s = new Slideshow(images, 500);

    s.next();
    expect((s.index, shown(s))).toBe('/a.jpg');
    vi.advanceTimersByTime(500);
    expect(s.layers[1 - s.front]).toBe('/b.jpg');
  });

  it('a dot shows that picture, and the timer starts over', () => {
    const s = new Slideshow(images, 500);
    const before = s.restarts;

    s.show(3);

    expect(s.index).toBe(3);
    expect(shown(s)).toBe('/c.jpg');
    expect(s.restarts).toBe(before + 1);
    vi.advanceTimersByTime(500);
    expect(s.layers[1 - s.front]).toBe('/hero.jpg'); // after the last comes the first
  });

  it('two quick clicks end on the second picture with the right one behind', () => {
    const s = new Slideshow(images, 500);

    s.show(2);
    s.show(1);
    vi.advanceTimersByTime(500);

    expect(shown(s)).toBe('/a.jpg');
    expect(s.layers[1 - s.front]).toBe('/b.jpg');
  });

  it('the dot of the picture already shown does nothing', () => {
    const s = new Slideshow(images, 500);

    s.show(0);

    expect(s.restarts).toBe(0);
    expect(shown(s)).toBe('/hero.jpg');
  });
});
