import type { BlenderOpenParams, BlenderStatusDto } from '$lib/rpc/types.gen';
import type { Tab } from '$lib/shell/tab.svelte';
import type { Session } from '$lib/stores/session.svelte';

/** Why Open in Blender cannot run now (null when it can); a missing or older add-on is offered for install instead. */
export function blockedReason(status: BlenderStatusDto | null): string | null {
  if (!status) return 'Looking for Blender…';
  if (!status.found || !status.supported) return status.problem ?? 'Blender was not found.';
  return null;
}

/** The Blender card's and the Open in Blender buttons' shared state: one per session. */
export class BlenderState {
  status = $state<BlenderStatusDto | null>(null);
  /** Blender is installing the add-on (two short Blender runs: a few seconds). */
  installing = $state(false);
  /** What the last install did, shown on the card until the next one. */
  installedNote = $state<string | null>(null);
  private loading: Promise<void> | null = null;

  constructor(private readonly session: Session) {}

  /** Asks the core once (again with force), e.g. when a card or button first shows. */
  ensure(tab: Tab, force = false): Promise<void> {
    if (!this.loading || force) {
      this.loading = (async () => {
        const status = await tab.quietly(() => this.session.rpc.call('blender.status'));
        if (status) this.status = status;
      })();
    }
    return this.loading;
  }

  async setPath(tab: Tab, path: string | null): Promise<void> {
    const status = await tab.safely(() => this.session.rpc.call('blender.setPath', { path }));
    if (status) this.status = status;
  }

  async install(tab: Tab): Promise<boolean> {
    this.installing = true;
    this.installedNote = null;
    try {
      const status = await tab.safely(() => this.session.rpc.call('blender.installAddon'));
      if (!status) return false;
      this.status = status;
      const time = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      this.installedNote = `Installed add-on ${status.addonInstalled ?? ''} into Blender ${status.version ?? ''} at ${time}. Restart Blender if it is open, so it loads the new add-on.`;
      tab.info(this.installedNote);
      return true;
    } finally {
      this.installing = false;
    }
  }

  /** The add-on must be installed or updated before Open in Blender works. */
  needsInstall(): boolean {
    const addon = this.status?.addon;
    return addon === 'missing' || addon === 'older' || addon === 'changed';
  }

  /** Opens in Blender; a missing or older add-on is installed first when the user agrees. */
  async open(tab: Tab, params: BlenderOpenParams): Promise<void> {
    await this.ensure(tab);
    const status = this.status;
    if (status && this.needsInstall()) {
      const message = status.addon === 'missing'
        ? `Install Tyrant's add-on into Blender ${status.version ?? ''}? Open in Blender needs it.`
        : `Update Tyrant's add-on in Blender ${status.version ?? ''} (${status.addonInstalled} → ${status.addonBundled})? This Tyrant needs the newer one.`;
      const ok = await this.session.platform.confirm(message, 'Tyrant add-on');
      if (!ok || !(await this.install(tab))) return;
    }
    const result = await tab.safely(() => this.session.rpc.call('blender.open', params));
    if (!result) return;
    tab.info(result.how === 'running' ? 'Opened in the running Blender.' : 'Started Blender with the model.');
    if (result.gameChanged) tab.warn('The game was updated since this Blender project was made. Use Start fresh for the new model.');
  }
}

const states = new WeakMap<Session, BlenderState>();

export function getBlender(session: Session): BlenderState {
  let state = states.get(session);
  if (!state) states.set(session, (state = new BlenderState(session)));
  return state;
}
