/**
 * Blender-style viewport controls as plain functions, tested without WebGL. The scene uses Unity's axes (animals face
 * +Z, their right side is +X, Y is up); the orbit camera sits at angle alpha around Y, measured from +X, and beta down from +Y.
 */

export type DragMode = 'orbit' | 'pan' | 'zoom';

export type KeyAction =
  | { kind: 'view'; alpha: number; beta: number }
  | { kind: 'orbit'; alpha: number; beta: number }
  | { kind: 'frame' }
  | { kind: 'ortho' };

/** Never exactly on a pole, where the orbit camera's angles break down. */
export const MIN_BETA = 0.01;
export const MAX_BETA = Math.PI - 0.01;
export const ROTATE_PER_PIXEL = 0.008;
const STEP = Math.PI / 12; // Blender's numpad 2/4/6/8 orbit step: 15°

/** The middle button as in Blender (Shift pans, Ctrl zooms); the left button does the same for touchpads; the right button pans. */
export function dragMode(button: number, shift: boolean, ctrl: boolean): DragMode | null {
  if (button === 0 || button === 1) return shift ? 'pan' : ctrl ? 'zoom' : 'orbit';
  if (button === 2) return 'pan';
  return null;
}

/** Numpad views as in Blender: 1 front, 3 right side, 7 top (Ctrl: the opposite side), 2/4/6/8 orbit, 5 orthographic, . frame. */
export function keyAction(code: string, ctrl: boolean): KeyAction | null {
  switch (code) {
    case 'Numpad1':
      return { kind: 'view', alpha: ctrl ? -Math.PI / 2 : Math.PI / 2, beta: Math.PI / 2 };
    case 'Numpad3':
      return { kind: 'view', alpha: ctrl ? Math.PI : 0, beta: Math.PI / 2 };
    case 'Numpad7': // from the front's side, so the head points to the bottom of the screen
      return { kind: 'view', alpha: Math.PI / 2, beta: ctrl ? MAX_BETA : MIN_BETA };
    case 'Numpad4':
      return { kind: 'orbit', alpha: -STEP, beta: 0 };
    case 'Numpad6':
      return { kind: 'orbit', alpha: STEP, beta: 0 };
    case 'Numpad8':
      return { kind: 'orbit', alpha: 0, beta: -STEP };
    case 'Numpad2':
      return { kind: 'orbit', alpha: 0, beta: STEP };
    case 'Numpad5':
      return { kind: 'ortho' };
    case 'NumpadDecimal':
    case 'Home':
      return { kind: 'frame' };
    default:
      return null;
  }
}

export function clampBeta(beta: number): number {
  return Math.min(MAX_BETA, Math.max(MIN_BETA, beta));
}

/** Wheel or Ctrl-drag zoom: a constant factor per step, so it feels the same at every distance. */
export function zoomed(radius: number, delta: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, radius * Math.exp(delta * 0.001)));
}

/** World units one screen pixel covers at the target, for panning that keeps the model under the mouse. */
export function unitsPerPixel(radius: number, fov: number, heightPx: number): number {
  return (2 * radius * Math.tan(fov / 2)) / Math.max(heightPx, 1);
}

/** The orthographic frustum that shows what the perspective view shows at the target. */
export function orthoExtents(radius: number, fov: number, aspect: number): { top: number; bottom: number; left: number; right: number } {
  const top = radius * Math.tan(fov / 2);
  return { top, bottom: -top, left: -top * aspect, right: top * aspect };
}
