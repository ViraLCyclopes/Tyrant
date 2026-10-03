import {
  ArcRotateCamera,
  Color4,
  DirectionalLight,
  Engine,
  HemisphericLight,
  Scene,
  SceneLoader,
  SkeletonViewer,
  Vector3,
  type AbstractMesh,
} from '@babylonjs/core';
import '@babylonjs/loaders/glTF'; // registers the .glb loader

export interface ModelViewer {
  setSkeleton(visible: boolean): void;
  dispose(): void;
}

/** Shows .glb files in a canvas with an orbit camera; the camera frames everything that loaded. */
export async function showModels(canvas: HTMLCanvasElement, urls: string[]): Promise<ModelViewer> {
  const engine = new Engine(canvas, true, { stencil: true });
  const scene = new Scene(engine);
  scene.clearColor = new Color4(0x12 / 255, 0x26 / 255, 0x1c / 255, 1);
  new HemisphericLight('sky', new Vector3(0, 1, 0), scene).intensity = 0.9;
  const sun = new DirectionalLight('sun', new Vector3(-0.5, -1, -0.4), scene);
  sun.intensity = 1.2;

  const camera = new ArcRotateCamera('camera', -Math.PI / 2.5, Math.PI / 2.6, 10, Vector3.Zero(), scene);
  camera.attachControl(canvas, true);

  const roots: AbstractMesh[] = [];
  const skeletons: SkeletonViewer[] = [];
  for (const url of urls) {
    // Asset-protocol URLs have no .glb extension, so name the loader explicitly.
    const result = await SceneLoader.ImportMeshAsync('', '', url, scene, undefined, '.glb');
    roots.push(result.meshes[0]);
    for (const skeleton of result.skeletons) {
      const mesh = result.meshes.find((m) => m.skeleton === skeleton);
      if (!mesh) continue;
      const viewer = new SkeletonViewer(skeleton, mesh, scene, false, 3, { displayMode: SkeletonViewer.DISPLAY_LINES });
      viewer.isEnabled = false;
      skeletons.push(viewer);
    }
  }

  let min = new Vector3(Infinity, Infinity, Infinity);
  let max = new Vector3(-Infinity, -Infinity, -Infinity);
  for (const root of roots) {
    const bounds = root.getHierarchyBoundingVectors(true);
    min = Vector3.Minimize(min, bounds.min);
    max = Vector3.Maximize(max, bounds.max);
  }
  const size = roots.length ? Vector3.Distance(min, max) || 1 : 1;
  camera.setTarget(roots.length ? Vector3.Center(min, max) : Vector3.Zero());
  camera.radius = size * 1.2;
  camera.lowerRadiusLimit = size / 100;
  camera.upperRadiusLimit = size * 20;
  camera.minZ = size / 1000;
  camera.maxZ = size * 100;
  camera.wheelPrecision = 100 / size;

  const observer = new ResizeObserver(() => engine.resize());
  observer.observe(canvas);
  engine.runRenderLoop(() => scene.render());

  return {
    setSkeleton: (visible) => skeletons.forEach((viewer) => (viewer.isEnabled = visible)),
    dispose: () => {
      observer.disconnect();
      engine.stopRenderLoop();
      skeletons.forEach((viewer) => viewer.dispose());
      scene.dispose();
      engine.dispose();
    },
  };
}
