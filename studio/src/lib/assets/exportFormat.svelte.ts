/** The model format of Assets exports and species packs (glb, FBX via Blender, or both), remembered on this PC. */
export type ExportFormat = 'glb' | 'fbx' | 'both';

const KEY = 'tyrant.exportFormat';

function load(): ExportFormat {
  try {
    const saved = localStorage.getItem(KEY);
    return saved === 'fbx' || saved === 'both' ? saved : 'glb';
  } catch {
    return 'glb';
  }
}

export const exportFormat = $state<{ value: ExportFormat }>({ value: load() });

export function setExportFormat(value: ExportFormat) {
  exportFormat.value = value;
  try {
    localStorage.setItem(KEY, value);
  } catch {
    /* storage blocked: the choice lasts this session */
  }
}

/** The params' extra field: nothing for glb, so existing calls stay as they were. */
export function formatParam(): { format?: ExportFormat } {
  return exportFormat.value === 'glb' ? {} : { format: exportFormat.value };
}
