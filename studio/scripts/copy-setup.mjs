// Copies the Setup program `npm run build:app` made to <repo>/release/, where it is easy to find and attach to a
// GitHub release (the folder is git-ignored: installers go on the Releases page, not in the repository).
import { copyFileSync, existsSync, mkdirSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';

const bundle = join('src-tauri', 'target', 'release', 'bundle', 'nsis');
const setups = existsSync(bundle) ? readdirSync(bundle).filter((f) => f.endsWith('-setup.exe')) : [];
if (setups.length === 0) throw new Error(`No Setup program in ${bundle}; run 'npm run build:app' (it builds it first).`);
const out = resolve('..', 'release');
mkdirSync(out, { recursive: true });
for (const setup of setups) copyFileSync(join(bundle, setup), join(out, setup));
console.log(`Setup: ${setups.map((s) => join(out, s)).join(', ')}`);
