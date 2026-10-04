import { render } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Tab, TAB_KEY } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import Backdrop from './Backdrop.svelte';
import { Slideshow } from './slideshow.svelte';

const images = ['/hero.jpg', '/backgrounds/bg-01.jpg', '/backgrounds/bg-02.jpg'];

function setup(active = true) {
  const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
  const tab = new Tab('tab-home', session.log, { retitle: () => {}, openTool: () => null });
  tab.active = active;
  const slideshow = new Slideshow(images, 500);
  const { container } = render(Backdrop, { props: { slideshow, interval: 1000 }, context: new Map<symbol, unknown>([[TAB_KEY, tab]]) });
  const shown = () => container.querySelector('.layer.on')?.getAttribute('data-image');
  return { tab, shown, slideshow };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('Backdrop', () => {
  it('starts on the first picture and moves to the next ones in turn, then round again', async () => {
    const { shown } = setup();
    expect(shown()).toBe('/hero.jpg');

    await vi.advanceTimersByTimeAsync(1000);
    expect(shown()).toBe('/backgrounds/bg-01.jpg');
    await vi.advanceTimersByTimeAsync(1000);
    expect(shown()).toBe('/backgrounds/bg-02.jpg'); // loaded behind during the last fade
    await vi.advanceTimersByTimeAsync(1000);
    expect(shown()).toBe('/hero.jpg'); // and round again
  });

  it('a picture picked by hand stays its full time before the next', async () => {
    const { shown, slideshow } = setup();
    await vi.advanceTimersByTimeAsync(900);

    slideshow.show(2);
    await vi.advanceTimersByTimeAsync(900);
    expect(shown()).toBe('/backgrounds/bg-02.jpg'); // the timer started over at the click
    await vi.advanceTimersByTimeAsync(100);
    expect(shown()).toBe('/hero.jpg');
  });

  it('stays on one picture while its tab is in the background', async () => {
    const { shown } = setup(false);

    await vi.advanceTimersByTimeAsync(5000);

    expect(shown()).toBe('/hero.jpg');
  });
});
