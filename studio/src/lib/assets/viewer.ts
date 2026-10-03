import {
  ArcRotateCamera,
  Camera,
  Color3,
  Color4,
  CubeTexture,
  DirectionalLight,
  Engine,
  HemisphericLight,
  MeshBuilder,
  PBRMaterial,
  Scene,
  SceneLoader,
  ShadowGenerator,
  SkeletonViewer,
  StandardMaterial,
  Texture,
  Vector3,
  type AbstractMesh,
  type BaseTexture,
  type Mesh,
  type Nullable,
} from '@babylonjs/core';
import type { GLTFFileLoader } from '@babylonjs/loaders/glTF';
import '@babylonjs/loaders/glTF'; // registers the .glb loader
import { clampBeta, dragMode, keyAction, orthoExtents, ROTATE_PER_PIXEL, unitsPerPixel, zoomed, type DragMode } from './navigation';
import { babylonFaces, linkedFile } from './viewerFiles';

/** A .glb preview file and the URL the page loads it from. */
export interface ModelFile {
  file: string;
  url: string;
}

export interface ViewerOptions {
  /** A local file as a URL the page may load (the asset protocol); used for the textures a .glb links. */
  fileUrl(path: string): string;
  /** Materials whose picture is a species skin, so a chosen skin replaces it. */
  skinnable: string[];
}

export interface ModelViewer {
  setSkeleton(visible: boolean): void;
  setTextures(visible: boolean): void;
  /** Shows a skin texture on the skinnable materials; null puts their own picture back. */
  setSkin(url: string | null): void;
  /** A terrain texture tiled on a floor under the model; null removes the floor. */
  setGround(url: string | null): void;
  /** Six faces in Unity's order (+X, -X, +Y, -Y, +Z, -Z); null for the plain background. */
  setSky(faces: string[] | null): void;
  /** Centres the model in the view, keeping the angle (Home / numpad .). */
  frame(): void;
  /** Stops drawing (the tab is hidden); resume starts again. */
  pause(): void;
  resume(): void;
  dispose(): void;
}

const BACKGROUND = new Color4(0x12 / 255, 0x26 / 255, 0x1c / 255, 1);
const GROUND_TILE_METRES = 4;

/** Shows .glb files with Blender-style navigation (navigation.ts). The scene uses Unity's axes: animals face +Z, right side +X. */
export async function showModels(canvas: HTMLCanvasElement, models: ModelFile[], options: ViewerOptions): Promise<ModelViewer> {
  const engine = new Engine(canvas, true, { stencil: true });
  const scene = new Scene(engine);
  scene.clearColor = BACKGROUND;
  new HemisphericLight('sky-light', new Vector3(0, 1, 0), scene).intensity = 0.9;
  const sun = new DirectionalLight('sun', new Vector3(-0.5, -1, -0.4), scene);
  sun.intensity = 1.2;
  const camera = new ArcRotateCamera('camera', Math.PI / 4, Math.PI / 2.6, 10, Vector3.Zero(), scene); // front-right, slightly above

  const roots: AbstractMesh[] = [];
  const skeletons: SkeletonViewer[] = [];
  for (const model of models) {
    // The .glb links its textures beside it; send those requests to the files there (see linkedFile).
    SceneLoader.OnPluginActivatedObservable.addOnce((plugin) => {
      if (plugin.name !== 'gltf') return;
      (plugin as GLTFFileLoader).preprocessUrlAsync = async (url) => {
        const file = linkedFile(url, model.url, model.file);
        return file ? options.fileUrl(file) : url;
      };
    });
    // Asset-protocol URLs have no .glb extension, so name the loader explicitly.
    const result = await SceneLoader.ImportMeshAsync('', '', model.url, scene, undefined, '.glb');
    roots.push(result.meshes[0]);
    for (const skeleton of result.skeletons) {
      const mesh = result.meshes.find((m) => m.skeleton === skeleton);
      if (!mesh) continue;
      const viewer = new SkeletonViewer(skeleton, mesh, scene, false, 3, { displayMode: SkeletonViewer.DISPLAY_LINES });
      viewer.isEnabled = false;
      skeletons.push(viewer);
    }
  }
  for (const mesh of scene.meshes) mesh.useVertexColors = false; // Unity's shaders use vertex colours as masks, not as tint

  let min = Vector3.Zero();
  let max = Vector3.Zero();
  if (roots.length) {
    min = new Vector3(Infinity, Infinity, Infinity);
    max = new Vector3(-Infinity, -Infinity, -Infinity);
    for (const root of roots) {
      const bounds = root.getHierarchyBoundingVectors(true);
      min = Vector3.Minimize(min, bounds.min);
      max = Vector3.Maximize(max, bounds.max);
    }
  }
  const center = Vector3.Center(min, max);
  const span = Vector3.Distance(min, max) || 1;
  camera.lowerRadiusLimit = span / 100;
  camera.upperRadiusLimit = span * 20;
  camera.minZ = span / 1000;
  camera.maxZ = span * 100;

  // Materials: remember each one's own textures, so textures can be turned off and skins swapped and put back.
  const skinnable = new Set(options.skinnable);
  const own = new Map<PBRMaterial, { albedo: Nullable<BaseTexture>; bump: Nullable<BaseTexture> }>();
  for (const material of scene.materials)
    if (material instanceof PBRMaterial) own.set(material, { albedo: material.albedoTexture, bump: material.bumpTexture });
  let texturesOn = true;
  let skin: Texture | null = null;
  const applyTextures = () => {
    for (const [material, textures] of own) {
      material.albedoTexture = !texturesOn ? null : skin && skinnable.has(material.name) ? skin : textures.albedo;
      material.bumpTexture = texturesOn ? textures.bump : null;
    }
  };

  sun.position = center.subtract(sun.direction.normalizeToNew().scale(span * 2));
  const shadows = new ShadowGenerator(2048, sun);
  shadows.useBlurExponentialShadowMap = true;
  shadows.blurKernel = 16;
  for (const root of roots) shadows.addShadowCaster(root, true);

  let ground: Mesh | null = null;
  const setGround = (url: string | null) => {
    ground?.material?.dispose(true, true);
    ground?.dispose();
    ground = null;
    if (!url) return;
    const size = Math.max(40, span * 6);
    ground = MeshBuilder.CreateGround('ground', { width: size, height: size }, scene);
    ground.position.y = min.y; // the animal stands on it
    const material = new StandardMaterial('ground', scene);
    const texture = new Texture(url, scene);
    texture.uScale = texture.vScale = size / GROUND_TILE_METRES;
    material.diffuseTexture = texture;
    material.specularColor = Color3.Black();
    ground.material = material;
    ground.receiveShadows = true;
  };

  let sky: Mesh | null = null;
  const setSky = (faces: string[] | null) => {
    sky?.material?.dispose(true, true);
    sky?.dispose();
    sky = null;
    if (!faces || faces.length !== 6) return;
    const material = new StandardMaterial('sky', scene);
    material.backFaceCulling = false;
    material.disableLighting = true;
    material.diffuseColor = Color3.Black();
    material.specularColor = Color3.Black();
    const texture = CubeTexture.CreateFromImages(babylonFaces(faces), scene);
    texture.coordinatesMode = Texture.SKYBOX_MODE;
    material.reflectionTexture = texture;
    sky = MeshBuilder.CreateBox('sky', { size: camera.maxZ / 2 }, scene);
    sky.material = material;
    sky.infiniteDistance = true; // stays around the camera however far it moves
    sky.isPickable = false;
  };

  let ortho = false;
  const fitOrtho = () => {
    if (!ortho) return;
    const extents = orthoExtents(camera.radius, camera.fov, engine.getAspectRatio(camera));
    camera.orthoTop = extents.top;
    camera.orthoBottom = extents.bottom;
    camera.orthoLeft = extents.left;
    camera.orthoRight = extents.right;
  };
  const zoom = (delta: number) => {
    camera.radius = zoomed(camera.radius, delta, camera.lowerRadiusLimit ?? 0, camera.upperRadiusLimit ?? Infinity);
    fitOrtho();
  };
  const frame = () => {
    camera.setTarget(center.clone(), false, false, true); // keep alpha/beta: only the target moves
    camera.radius = span * 1.2;
    fitOrtho();
  };

  // Blender-style input instead of Babylon's default camera controls (no attachControl).
  canvas.tabIndex = 0; // numpad views need keyboard focus
  let drag: { mode: DragMode; x: number; y: number } | null = null;
  const onPointerDown = (e: PointerEvent) => {
    const mode = dragMode(e.button, e.shiftKey, e.ctrlKey);
    if (!mode) return;
    e.preventDefault(); // no autoscroll on middle-click
    canvas.focus();
    canvas.setPointerCapture(e.pointerId);
    drag = { mode, x: e.clientX, y: e.clientY };
  };
  const onPointerMove = (e: PointerEvent) => {
    if (!drag) return;
    const dx = e.clientX - drag.x;
    const dy = e.clientY - drag.y;
    drag.x = e.clientX;
    drag.y = e.clientY;
    if (drag.mode === 'orbit') {
      camera.alpha -= dx * ROTATE_PER_PIXEL;
      camera.beta = clampBeta(camera.beta - dy * ROTATE_PER_PIXEL);
    } else if (drag.mode === 'pan') {
      const step = unitsPerPixel(camera.radius, camera.fov, canvas.clientHeight);
      const right = camera.getDirection(Vector3.Right()).scale(-dx * step);
      const up = camera.getDirection(Vector3.Up()).scale(dy * step);
      camera.setTarget(camera.target.add(right).add(up), false, false, true);
    } else {
      zoom(dy * 5);
    }
  };
  const onPointerUp = (e: PointerEvent) => {
    if (!drag) return;
    drag = null;
    if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
  };
  const onWheel = (e: WheelEvent) => {
    e.preventDefault(); // zoom instead of scrolling the panel
    zoom(e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY);
  };
  const onKeyDown = (e: KeyboardEvent) => {
    const action = keyAction(e.code, e.ctrlKey);
    if (!action) return;
    e.preventDefault();
    if (action.kind === 'view') {
      camera.alpha = action.alpha;
      camera.beta = action.beta;
    } else if (action.kind === 'orbit') {
      camera.alpha += action.alpha;
      camera.beta = clampBeta(camera.beta + action.beta);
    } else if (action.kind === 'ortho') {
      ortho = !ortho;
      camera.mode = ortho ? Camera.ORTHOGRAPHIC_CAMERA : Camera.PERSPECTIVE_CAMERA;
      fitOrtho();
    } else {
      frame();
    }
  };
  const noMenu = (e: Event) => e.preventDefault(); // the right button pans; middle-click opens nothing
  const listeners: [string, EventListener, AddEventListenerOptions?][] = [
    ['pointerdown', onPointerDown as EventListener],
    ['pointermove', onPointerMove as EventListener],
    ['pointerup', onPointerUp as EventListener],
    ['pointercancel', onPointerUp as EventListener],
    ['wheel', onWheel as EventListener, { passive: false }],
    ['keydown', onKeyDown as EventListener],
    ['contextmenu', noMenu],
    ['auxclick', noMenu],
  ];
  for (const [type, listener, opts] of listeners) canvas.addEventListener(type, listener, opts);

  frame();
  const observer = new ResizeObserver(() => {
    engine.resize();
    fitOrtho();
  });
  observer.observe(canvas);
  engine.runRenderLoop(() => scene.render());

  return {
    setSkeleton: (visible) => skeletons.forEach((viewer) => (viewer.isEnabled = visible)),
    setTextures: (visible) => {
      texturesOn = visible;
      applyTextures();
    },
    setSkin: (url) => {
      const previous = skin;
      skin = url ? new Texture(url, scene, false, false) : null; // invertY false, like the glTF loader: its UVs are already flipped
      applyTextures();
      previous?.dispose();
    },
    setGround,
    setSky,
    frame,
    pause: () => engine.stopRenderLoop(),
    resume: () => {
      engine.stopRenderLoop(); // never two loops
      engine.runRenderLoop(() => scene.render());
    },
    dispose: () => {
      for (const [type, listener] of listeners) canvas.removeEventListener(type, listener);
      observer.disconnect();
      engine.stopRenderLoop();
      skeletons.forEach((viewer) => viewer.dispose());
      scene.dispose();
      engine.dispose();
    },
  };
}
