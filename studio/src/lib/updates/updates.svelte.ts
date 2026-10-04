import type { AvailableUpdate } from '$lib/platform';
import type { KeyValueStore } from '$lib/storage';
import type { Session } from '$lib/stores/session.svelte';

const LAST_CHECK = 'tyrant.updates.lastCheck';
const DAY = 24 * 60 * 60 * 1000;
const RELEASES = 'https://github.com/ViraLCyclopes/Tyrant/releases';

export type UpdateOffer =
  | { kind: 'github'; version: string; notes: string; nexusUrl: string | null }
  | { kind: 'nexus'; version: string; url: string };

/** Tyrant's own updates: GitHub installs them, Nexus only notifies (spec: release plan 2). */
export class Updates {
  offer = $state<UpdateOffer | null>(null);
  checking = $state(false);
  progress = $state<number | null>(null);
  note = $state<string | null>(null);

  constructor(
    private readonly session: Session,
    private readonly storage: KeyValueStore,
    private readonly enabled: () => boolean,
    private readonly now: () => number = () => Date.now(),
  ) {}

  /** At start: at most once a day, silently. */
  async startup(): Promise<void> {
    if (!this.enabled()) return;
    const last = Number(this.storage.get(LAST_CHECK) ?? 0);
    if (this.now() - last < DAY) return;
    this.storage.set(LAST_CHECK, String(this.now()));
    await this.run(false);
  }

  /** Help → Check for updates: always, and says what it found. */
  async check(): Promise<void> {
    this.storage.set(LAST_CHECK, String(this.now()));
    await this.run(true);
  }

  async install(): Promise<void> {
    if (this.offer?.kind !== 'github') return;
    if (this.session.busy) {
      this.note = `Wait for ${this.session.job?.title ?? 'the running job'} to finish, then update.`;
      return;
    }
    this.progress = 0;
    try {
      await this.session.platform.installUpdate((fraction) => (this.progress = fraction));
    } catch (e) {
      this.progress = null;
      this.note = `The update could not be installed; nothing changed (${e instanceof Error ? e.message : String(e)}).`;
    }
  }

  /** The native side (opening the Nexus page). */
  get platform() {
    return this.session.platform;
  }

  dismiss(): void {
    this.offer = null;
    this.note = null;
  }

  private async run(manual: boolean): Promise<void> {
    this.checking = true;
    this.note = null;
    try {
      const current = await this.session.platform.appVersion();
      let github: AvailableUpdate | null | 'unavailable' = null;
      let failure: string | null = null;
      try {
        github = await this.session.platform.checkForUpdate();
      } catch (e) {
        failure = e instanceof Error ? e.message : String(e);
      }
      const nexus = await this.session.rpc.call('app.checkNexus').catch(() => null);
      const nexusNewer = nexus?.version && nexus.url && compareVersions(nexus.version, current) > 0 ? { version: nexus.version, url: nexus.url } : null;
      if (github && github !== 'unavailable') {
        this.offer = { kind: 'github', version: github.version, notes: github.notes, nexusUrl: nexus?.version === github.version ? (nexus.url ?? null) : null };
      } else if (nexusNewer) {
        this.offer = { kind: 'nexus', ...nexusNewer };
      } else {
        this.offer = null;
      }
      if (!manual) return;
      if (failure) this.note = `The update check failed: ${failure}`;
      else if (github === 'unavailable' && !this.offer) this.note = `Updates aren't set up in this build; download new versions from ${RELEASES}.`;
      else if (!this.offer) this.note = `You're up to date (Tyrant ${current}).`;
    } finally {
      this.checking = false;
    }
  }
}

/** "v0.2.0" / "0.2.0" / "0.2.0-beta": a pre-release sorts before its release; unparsable sorts lowest. */
export function compareVersions(a: string, b: string): number {
  const parse = (v: string) => {
    const m = /^v?(\d+)\.(\d+)\.(\d+)(?:-(.+))?$/.exec(v.trim());
    return m ? { n: [Number(m[1]), Number(m[2]), Number(m[3])], pre: m[4] ?? null } : null;
  };
  const x = parse(a);
  const y = parse(b);
  if (!x || !y) return x ? 1 : y ? -1 : 0;
  for (let i = 0; i < 3; i++) if (x.n[i] !== y.n[i]) return x.n[i] - y.n[i];
  if (x.pre === y.pre) return 0;
  if (x.pre === null) return 1;
  if (y.pre === null) return -1;
  return x.pre < y.pre ? -1 : 1;
}
