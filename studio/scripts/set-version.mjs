// Sets Tyrant's version everywhere it is written: node scripts/set-version.mjs 0.2.0 (npm run set-version 0.2.0).
// The framework keeps its own version (FrameworkInfo.Version and game/Tyrant.Framework's <Version>).
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const version = process.argv[2];
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version ?? '')) {
  console.error('Usage: npm run set-version <major.minor.patch>, e.g. npm run set-version 0.2.0');
  process.exit(1);
}
const studio = resolve(import.meta.dirname, '..');
const edits = [
  [resolve(studio, 'package.json'), /("version":\s*")[^"]+(")/],
  [resolve(studio, 'src-tauri', 'tauri.conf.json'), /("version":\s*")[^"]+(")/],
  [resolve(studio, 'src-tauri', 'Cargo.toml'), /(^version = ")[^"]+(")/m],
  [resolve(studio, '..', 'Directory.Build.props'), /(<Version>)[^<]+(<\/Version>)/],
  [resolve(studio, '..', 'tools', 'blender', 'tyrant_blender', 'blender_manifest.toml'), /(^version = ")[^"]+(")/m],
];
for (const [file, pattern] of edits) {
  const text = readFileSync(file, 'utf8');
  if (!pattern.test(text)) throw new Error(`No version found in ${file}`);
  writeFileSync(file, text.replace(pattern, `$1${version}$2`));
  console.log(`${file}: ${version}`);
}
