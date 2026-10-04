/**
 * Which start-screen picture is shown. Two layers take turns: the hidden one already holds the next picture (so it is loaded
 * before it fades in), and gets the one after it once the fade is over. The timer lives in Backdrop; the dots call show().
 */
export class Slideshow {
  index = $state(0);
  front = $state(0);
  layers = $state<string[]>([]);
  /** Bumped by a dot: Backdrop starts its timer over, so a picture picked by hand gets its full time. */
  restarts = $state(0);
  private pending: ReturnType<typeof setTimeout> | undefined;

  constructor(
    readonly images: readonly string[],
    readonly fade: number,
  ) {
    this.layers = [images[0] ?? '', images[1] ?? images[0] ?? ''];
  }

  /** The timer: the following picture. */
  next(): void {
    if (this.images.length > 1) this.go((this.index + 1) % this.images.length);
  }

  /** A dot: that picture now. */
  show(index: number): void {
    if (index === this.index || index < 0 || index >= this.images.length) return;
    this.go(index);
    this.restarts++;
  }

  dispose(): void {
    clearTimeout(this.pending);
  }

  private go(index: number): void {
    const behind = 1 - this.front;
    this.layers[behind] = this.images[index]; // usually there already (the next one)
    this.front = behind;
    this.index = index;
    clearTimeout(this.pending);
    const hidden = 1 - behind;
    const following = this.images[(index + 1) % this.images.length];
    this.pending = setTimeout(() => (this.layers[hidden] = following), this.fade);
  }
}
