// Makes a portable Tyrant: studio/portable/Tyrant/Tyrant.exe plus the core it starts (sidecar/), no installer.
// Run through `npm run build:portable`, which builds both first.
import { cpSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';

const app = join('src-tauri', 'target', 'release', 'tyrant-app.exe');
const core = join('src-tauri', 'sidecar');
const out = join('portable', 'Tyrant');

for (const path of [app, join(core, 'tyrant.exe')]) {
  if (!existsSync(path)) throw new Error(`${path} is missing; run 'npm run build:portable' (it builds it first).`);
}
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
cpSync(app, join(out, 'Tyrant.exe'));
cpSync(core, join(out, 'sidecar'), { recursive: true }); // the app looks for sidecar\tyrant.exe next to itself
console.log(`Portable Tyrant: ${join(process.cwd(), out, 'Tyrant.exe')}`);
