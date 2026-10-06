import type { AvailableUpdate } from '$lib/platform';
import type { KeyValueStore } from '$lib/storage';
import type { Session } from '$lib/stores/session.svelte';

const LAST_CHECK = 'tyrant.updates.lastCheck';
const DAY = 24 * 60 * 60 * 1000;
export const RELEASES = 'https://github.com/ViraLCyclopes/Tyrant/releases';

export type UpdateOffer = { version: string; notes: string };

/** Tyrant's own updates, from GitHub releases. */
export class Updates {
  offer = $state<UpdateOffer | null>(null);
  checking = $state(false);
  /** Downloading and installing: Update now stays hidden and Tyrant counts as busy (no job may start). */
  installing = $state(false);
  /** 0..1 while downloading; null when the size is unknown. */
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
    if (!this.offer || this.installing) return;
    if (this.session.busy) {
      this.note = `Wait for ${this.session.job?.title ?? 'the running job'} to finish, then update.`;
      return;
    }
    this.installing = true;
    this.progress = 0;
    // The installer closes Tyrant when it starts: hold the job slot so nothing starts that it would cut off.
    this.session.job = { id: null, title: 'Update Tyrant', fraction: 0, message: 'Downloading the update', cancel: null };
    try {
      await this.session.platform.installUpdate((fraction) => {
        this.progress = fraction;
        if (this.session.job) this.session.job = { ...this.session.job, fraction: fraction ?? 0 };
      });
    } catch (e) {
      this.note = `The update could not be installed; nothing changed (${e instanceof Error ? e.message : String(e)}).`;
    } finally {
      this.installing = false;
      this.progress = null;
      if (this.session.job?.title === 'Update Tyrant') this.session.job = null;
    }
  }

  /** The native side (opening the release page). */
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
      this.offer = github && github !== 'unavailable' ? { version: github.version, notes: github.notes } : null;
      if (!manual) return;
      if (failure) this.note = `The update check failed: ${failure}`;
      else if (github === 'unavailable' && !this.offer) this.note = `Updates aren't set up in this build; download new versions from ${RELEASES}.`;
      else if (!this.offer) this.note = `You're up to date (Tyrant ${current}).`;
    } finally {
      this.checking = false;
    }
  }
}
