// One-time: makes Tyrant's update-signing key. The private key stays on your PC (and in GitHub's secrets); the public key goes into
// src-tauri/updater-key.pub, which is committed. See docs/making-a-release.md.
import { execSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';

const dir = join(homedir(), '.tyrant');
const key = join(dir, 'tyrant-updater.key');
if (existsSync(key)) {
  console.error(`${key} already exists; delete it first only if you really want a new key (installed copies would stop accepting updates).`);
  process.exit(1);
}
mkdirSync(dir, { recursive: true });
execSync(`npx tauri signer generate -w "${key}"`, { stdio: 'inherit' }); // asks for a password
copyFileSync(`${key}.pub`, resolve(import.meta.dirname, '..', 'src-tauri', 'updater-key.pub'));
console.log(`Private key: ${key} (back it up somewhere safe; never commit it).`);
console.log('Public key copied to src-tauri/updater-key.pub: commit that file.');
console.log('Then add the private key file content and its password as GitHub secrets TAURI_SIGNING_PRIVATE_KEY and TAURI_SIGNING_PRIVATE_KEY_PASSWORD.');
