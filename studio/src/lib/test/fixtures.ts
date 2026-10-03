import { render } from '@testing-library/svelte';
import type { Component } from 'svelte';
import type { InstallInfo, WorkspaceStatus } from '$lib/rpc/types.gen';
import { SESSION_KEY, type Session } from '$lib/stores/session.svelte';

export function installInfo(overrides: Partial<InstallInfo> = {}): InstallInfo {
  return { rootDir: 'G:\\PK', steamAppId: '666150', buildGuid: 'b1', ...overrides };
}

export function workspaceStatus(overrides: Partial<WorkspaceStatus> = {}): WorkspaceStatus {
  return {
    dir: 'D:\\ws',
    gameRoot: 'G:\\PK',
    steamAppId: '666150',
    buildGuid: 'b1',
    stale: false,
    outputs: [],
    dumper: 'notInstalled',
    hasData: false,
    hasAssetIndex: false,
    hasSource: false,
    framework: 'missing',
    ...overrides,
  };
}

/** Renders a component that reads the session from context (test helper, so props are loosely typed). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function renderWith(component: Component<any>, session: Session, props: Record<string, unknown> = {}) {
  return render(component, { props, context: new Map([[SESSION_KEY, session]]) });
}
