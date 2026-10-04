import {
  ArcRotateCamera,
  Camera,
  Color3,
  Color4,
  CubeTexture,
  DirectionalLight,
  Engine,
  HemisphericLight,
  ImportMeshAsync,
  Material,
  MeshBuilder,
  PBRMaterial,
  Scene,
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
import '@babylonjs/loaders/glTF'; // registers the .glb loader
import type { ModSampledColors, PreviewMaterial } from '$lib/rpc/types.gen';
import { clampBeta, dragMode, keyAction, orthoExtents, orthoZoom, ROTATE_PER_PIXEL, unitsPerPixel, type DragMode } from './navigation';
import { PkAnimalPlugin } from './pkAnimalPlugin';
import { babylonFaces, linkedFile, loadParts } from './viewerFiles';
import { animalUniforms, planMaterial } from './viewerMaterials';

/** A .glb preview file and the URL the page loads it from. */
export interface ModelFile {
  file: string;
  url: string;
}

export interface ViewerOptions {
  /** A local file as a URL the page may load (the asset protocol); used for the textures a .glb links. */
  fileUrl(path: string): string;
  /** The preview's materials: how each is dressed (animal plugin, cutout) and which take a chosen skin. */
  materials: PreviewMaterial[];
}

/** A skin's maps as URLs; a missing map keeps the model's own. */
export interface AnimalMaps {
  diffuse?: string;
  normal?: string;
  extra?: string;
  pattern?: string;
}

export interface ModelViewer {
  /** One line per .glb part that could not be loaded ("<file>: <why>"); the other parts are shown. */
  failures: string[];
  setSkeleton(visible: boolean): void;
  setTextures(visible: boolean): void;
  /** Shows a skin texture on the skinnable materials; null puts their own picture back. */
  setSkin(url: string | null): void;
  /** Shows a skin's maps on the skinnable materials; null puts their own back. */
  setAnimalMaps(maps: AnimalMaps | null): void;
  /** One animal's colours on the animal materials; null leaves the textures untouched. */
  setColors(colors: ModSampledColors | null): void;
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
  try {
    return await build(canvas, models, options, engine, scene);
  } catch (e) {
    scene.dispose(); // never leak the WebGL context (C1, C10)
    engine.dispose();
    throw e;
  }
}

async function build(canvas: HTMLCanvasElement, models: ModelFile[], options: ViewerOptions, engine: Engine, scene: Scene): Promise<ModelViewer> {
  scene.clearColor = BACKGROUND;
  new HemisphericLight('sky-light', new Vector3(0, 1, 0), scene).intensity = 0.9;
  const sun = new DirectionalLight('sun', new Vector3(-0.5, -1, -0.4), scene);
  sun.intensity = 1.2;
  const camera = new ArcRotateCamera('camera', Math.PI / 4, Math.PI / 2.6, 10, Vector3.Zero(), scene); // front-right, slightly above

  const roots: AbstractMesh[] = [];
  const skeletons: SkeletonViewer[] = [];
  const { loaded, failures } = await loadParts(models, (model) =>
    ImportMeshAsync(model.url, scene, {
      pluginExtension: '.glb', // asset-protocol URLs have no .glb extension
      pluginOptions: {
        gltf: {
          // The .glb links its textures beside it; send those requests to the files there (see linkedFile). Per load, not global (C10).
          preprocessUrlAsync: async (url: string) => {
            const file = linkedFile(url, model.url, model.file);
            return file ? options.fileUrl(file) : url;
          },
        },
      },
    }),
  );
  if (loaded.length === 0) throw new Error(failures.join(' ') || 'No part of the model could be loaded.');
  for (const result of loaded) {
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

  // Materials: each is dressed by its plan (animal plugin, cutout); remember its own textures, so textures can be turned
  // off and skins swapped and put back.
  const skinnable = new Set(options.materials.filter((m) => m.skinnable).map((m) => m.name));
  const plans = new Map(options.materials.map((m) => [m.name, planMaterial(m)]));
  const load = (url: string | null | undefined) => (url ? new Texture(url, scene, false, false) : null); // invertY false, like the glTF loader
  type Own = { albedo: Nullable<BaseTexture>; bump: Nullable<BaseTexture>; extra: Nullable<BaseTexture>; pattern: Nullable<BaseTexture> };
  const own = new Map<PBRMaterial, Own>();
  const plugins = new Map<PBRMaterial, PkAnimalPlugin>();
  for (const material of scene.materials) {
    if (!(material instanceof PBRMaterial)) continue;
    const plan = plans.get(material.name);
    if (plan?.cutoff != null && material.albedoTexture) {
      material.albedoTexture.hasAlpha = true;
      material.transparencyMode = PBRMaterial.PBRMATERIAL_ALPHATEST;
      material.alphaCutOff = plan.cutoff;
    }
    const textures: Own = { albedo: material.albedoTexture, bump: material.bumpTexture, extra: null, pattern: null };
    if (plan?.kind === 'animal') {
      material.metallic = 0; // metallic workflow, so the plugin can set roughness from the extra map
      material.roughness = 1;
      const plugin = new PkAnimalPlugin(material);
      plugin.extra = textures.extra = load(plan.extra && options.fileUrl(plan.extra));
      plugin.pattern = textures.pattern = load(plan.pattern && options.fileUrl(plan.pattern));
      plugins.set(material, plugin);
    }
    own.set(material, textures);
  }
  let texturesOn = true;
  let skin: { diffuse: Nullable<Texture>; normal: Nullable<Texture>; extra: Nullable<Texture>; pattern: Nullable<Texture> } | null = null;
  const applyTextures = () => {
    for (const [material, textures] of own) {
      const worn = skin && skinnable.has(material.name) ? skin : null;
      material.albedoTexture = !texturesOn ? null : (worn?.diffuse ?? textures.albedo);
      if (material.albedoTexture && plans.get(material.name)?.cutoff != null) material.albedoTexture.hasAlpha = true;
      material.bumpTexture = !texturesOn ? null : (worn?.normal ?? textures.bump);
      const plugin = plugins.get(material);
      if (plugin) {
        plugin.extra = !texturesOn ? null : (worn?.extra ?? textures.extra);
        plugin.pattern = !texturesOn ? null : (worn?.pattern ?? textures.pattern);
        material.markAsDirty(Material.TextureDirtyFlag);
      }
    }
  };
  const setAnimalMaps = (maps: AnimalMaps | null) => {
    const previous = skin;
    skin = maps ? { diffuse: load(maps.diffuse), normal: load(maps.normal), extra: load(maps.extra), pattern: load(maps.pattern) } : null;
    applyTextures();
    for (const texture of [previous?.diffuse, previous?.normal, previous?.extra, previous?.pattern]) texture?.dispose();
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
  let orthoRadius = camera.radius; // the orthographic view's scale; zooming there never moves the camera (C9)
  const fitOrtho = () => {
    if (!ortho) return;
    const extents = orthoExtents(orthoRadius, camera.fov, engine.getAspectRatio(camera));
    camera.orthoTop = extents.top;
    camera.orthoBottom = extents.bottom;
    camera.orthoLeft = extents.left;
    camera.orthoRight = extents.right;
  };
  const zoom = (delta: number) => {
    const next = orthoZoom({ radius: camera.radius, orthoRadius }, ortho, delta, camera.lowerRadiusLimit ?? 0, camera.upperRadiusLimit ?? Infinity);
    camera.radius = next.radius;
    orthoRadius = next.orthoRadius;
    fitOrtho();
  };
  const frame = () => {
    camera.setTarget(center.clone(), false, false, true); // keep alpha/beta: only the target moves
    camera.radius = orthoRadius = span * 1.2;
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
      if (ortho) orthoRadius = camera.radius;
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
    failures,
    setSkeleton: (visible) => skeletons.forEach((viewer) => (viewer.isEnabled = visible)),
    setTextures: (visible) => {
      texturesOn = visible;
      applyTextures();
    },
    setSkin: (url) => setAnimalMaps(url ? { diffuse: url } : null),
    setAnimalMaps,
    setColors: (colors) => {
      const uniforms = animalUniforms(colors);
      for (const plugin of plugins.values()) plugin.uniforms = uniforms;
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
