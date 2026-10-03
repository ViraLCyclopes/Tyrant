import type { AssetIndexRunResult, DecompileRunResult, DumpRunResult, InstallState, RefreshAllResult } from '$lib/rpc/types.gen';

export function outputLabel(name: string): string {
  if (name === 'data') return 'Game data';
  if (name === 'assets/index') return 'Asset index';
  if (name.startsWith('source/')) return `Code: ${name.slice('source/'.length)}`;
  return name;
}

export function dumperLabel(state: InstallState): string {
  switch (state) {
    case 'installed':
      return 'Installed';
    case 'loaderOnly':
      return 'MelonLoader is present; the dumper is not installed';
    case 'conflict':
      return 'Blocked by another mod loader';
    default:
      return 'Not installed';
  }
}

export function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

export function summarizeDump(r: DumpRunResult): string {
  const warnings = r.errors.length ? ` (${r.errors.length} warnings; Copy diagnostics has the details)` : '';
  return `Dumped ${r.objects.toLocaleString('en-US')} objects of ${r.types} types and ${r.languages} languages${warnings}.`;
}

export function summarizeDecompile(r: DecompileRunResult): string {
  const failed = r.assemblies.filter((a) => !a.success);
  const done = r.assemblies.length - failed.length;
  return failed.length === 0
    ? `Decompiled ${done} assemblies.`
    : `Decompiled ${done} of ${r.assemblies.length} assemblies. ${failed.map((a) => a.error).join(' ')}`;
}

export function summarizeIndex(r: AssetIndexRunResult): string {
  return `Indexed ${r.assets.toLocaleString('en-US')} assets${r.failures ? `; ${r.failures} bundles could not be read` : ''}.`;
}

export function summarizeRefresh(r: RefreshAllResult): string {
  return r.steps.map((s) => (s.status === 'ok' ? `${s.name}: done` : `${s.name}: ${s.status} — ${s.message}`)).join(' · ');
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
