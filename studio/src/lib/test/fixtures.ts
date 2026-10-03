import { render } from '@testing-library/svelte';
import type { Component } from 'svelte';
import type { InstallInfo, WorkspaceStatus } from '$lib/rpc/types.gen';
import { TAB_KEY, Tab } from '$lib/shell/tab.svelte';
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

/** A tab logging into the session's log, with a host that opens nothing. */
export function testTab(session: Session, id = 'tab-test'): Tab {
  return new Tab(id, session.log, { retitle: () => {}, openTool: () => null });
}

/** Messages a tab's panel would show, oldest first (`null`: core-wide records only). */
export function messages(session: Session, tab: Tab | string | null): string[] {
  const id = tab === null ? null : typeof tab === 'string' ? tab : tab.id;
  return (id === null ? session.log.records.filter((r) => r.tab === null) : session.log.forTab(id)).map((r) => r.message);
}

/** Renders a component that reads the session and its tab from context (test helper, so props are loosely typed). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function renderWith(component: Component<any>, session: Session, props: Record<string, unknown> = {}, tab: Tab = testTab(session)) {
  return render(component, { props, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, tab]]) });
}
