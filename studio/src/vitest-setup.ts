import '@testing-library/jest-dom/vitest';

// jsdom has no PointerEvent: without it fireEvent.pointer* sends a plain Event with no button or coordinates.
if (typeof globalThis.PointerEvent === 'undefined') {
  class PointerEventStub extends MouseEvent {
    readonly pointerId: number;
    constructor(type: string, init: PointerEventInit = {}) {
      super(type, init);
      this.pointerId = init.pointerId ?? 0;
    }
  }
  globalThis.PointerEvent = PointerEventStub as unknown as typeof PointerEvent;
}
