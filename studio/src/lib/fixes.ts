const labels: Record<string, string> = {
  PICK_GAME_FOLDER: 'Pick game folder',
  PICK_WORKSPACE_FOLDER: 'Pick workspace folder',
  REFRESH_WORKSPACE: 'Refresh all',
  INSTALL_DUMPER: 'Install dumper',
};

/** Button label for an error's fix action; null when this version of the app has no button for it. */
export function fixLabel(fix: string): string | null {
  return labels[fix] ?? null;
}
