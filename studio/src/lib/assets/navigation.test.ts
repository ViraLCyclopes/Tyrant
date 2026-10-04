import { describe, expect, it } from 'vitest';
import { clampBeta, dragMode, keyAction, MAX_BETA, MIN_BETA, orthoExtents, unitsPerPixel, zoomed, orthoZoom } from './navigation';

describe('dragMode', () => {
  it('maps the middle button like Blender: orbit, Shift pans, Ctrl zooms', () => {
    expect(dragMode(1, false, false)).toBe('orbit');
    expect(dragMode(1, true, false)).toBe('pan');
    expect(dragMode(1, false, true)).toBe('zoom');
  });

  it('lets the left button do the same for touchpads, and the right button pan', () => {
    expect(dragMode(0, false, false)).toBe('orbit');
    expect(dragMode(0, true, false)).toBe('pan');
    expect(dragMode(2, false, false)).toBe('pan');
    expect(dragMode(3, false, false)).toBeNull();
  });
});

describe('keyAction', () => {
  it('views the animal from the front, its right side or the top with numpad 1, 3 and 7', () => {
    expect(keyAction('Numpad1', false)).toEqual({ kind: 'view', alpha: Math.PI / 2, beta: Math.PI / 2 });
    expect(keyAction('Numpad3', false)).toEqual({ kind: 'view', alpha: 0, beta: Math.PI / 2 });
    expect(keyAction('Numpad7', false)).toEqual({ kind: 'view', alpha: Math.PI / 2, beta: MIN_BETA });
  });

  it('views from the opposite side with Ctrl', () => {
    expect(keyAction('Numpad1', true)).toEqual({ kind: 'view', alpha: -Math.PI / 2, beta: Math.PI / 2 });
    expect(keyAction('Numpad3', true)).toEqual({ kind: 'view', alpha: Math.PI, beta: Math.PI / 2 });
    expect(keyAction('Numpad7', true)).toEqual({ kind: 'view', alpha: Math.PI / 2, beta: MAX_BETA });
  });

  it('orbits in 15° steps, toggles orthographic and frames', () => {
    expect(keyAction('Numpad4', false)).toEqual({ kind: 'orbit', alpha: -Math.PI / 12, beta: 0 });
    expect(keyAction('Numpad6', false)).toEqual({ kind: 'orbit', alpha: Math.PI / 12, beta: 0 });
    expect(keyAction('Numpad8', false)).toEqual({ kind: 'orbit', alpha: 0, beta: -Math.PI / 12 });
    expect(keyAction('Numpad2', false)).toEqual({ kind: 'orbit', alpha: 0, beta: Math.PI / 12 });
    expect(keyAction('Numpad5', false)).toEqual({ kind: 'ortho' });
    expect(keyAction('NumpadDecimal', false)).toEqual({ kind: 'frame' });
    expect(keyAction('Home', false)).toEqual({ kind: 'frame' });
    expect(keyAction('KeyA', false)).toBeNull();
  });
});

describe('camera math', () => {
  it('keeps the camera off the poles', () => {
    expect(clampBeta(0)).toBe(MIN_BETA);
    expect(clampBeta(4)).toBe(MAX_BETA);
    expect(clampBeta(1)).toBe(1);
  });

  it('zooms by a constant factor within the limits', () => {
    expect(zoomed(10, 100, 1, 100)).toBeCloseTo(10 * Math.exp(0.1));
    expect(zoomed(10, -100000, 1, 100)).toBe(1);
    expect(zoomed(10, 100000, 1, 100)).toBe(100);
  });

  it('sizes the orthographic view to what the perspective view shows at the target', () => {
    const extents = orthoExtents(10, Math.PI / 2, 2);

    expect(extents.top).toBeCloseTo(10);
    expect(extents.bottom).toBeCloseTo(-10);
    expect(extents.right).toBeCloseTo(20);
    expect(extents.left).toBeCloseTo(-20);
    expect(unitsPerPixel(10, Math.PI / 2, 200)).toBeCloseTo(0.1);
  });
});

describe('orthoZoom', () => {
  it('in orthographic view zoom changes the scale and never moves the camera', () => {
    const next = orthoZoom({ radius: 10, orthoRadius: 10 }, true, 120, 0.1, 200);
    expect(next.radius).toBe(10);
    expect(next.orthoRadius).toBeGreaterThan(10);
  });

  it('in perspective view zoom moves the camera', () => {
    const next = orthoZoom({ radius: 10, orthoRadius: 10 }, false, 120, 0.1, 200);
    expect(next.radius).toBeGreaterThan(10);
    expect(next.orthoRadius).toBe(10);
  });
});
