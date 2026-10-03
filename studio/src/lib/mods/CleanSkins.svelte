<script lang="ts">
  import { onMount } from 'svelte';
  import type { OrphanSkinRow } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  const session = getSession();
  const tab = getTab();
  let orphans = $state<OrphanSkinRow[] | null>(null);
  let unreadable = $state<string | null>(null);
  let chosen = $state<string[]>([]);

  onMount(async () => {
    try {
      orphans = (await session.rpc.call('mods.skinSlots')).orphans;
    } catch (e) {
      unreadable = e instanceof Error ? e.message : String(e); // not "nothing to clean up": the file could not be read
    }
  });

  function toggle(key: string, on: boolean) {
    chosen = on ? [...chosen, key] : chosen.filter((k) => k !== key);
  }

  async function forget() {
    const message = `Forget ${chosen.length} skin number(s)? Animals in saved parks that still wear those skins will show their species' first skin, or whichever new skin later takes the number. The game must be closed.`;
    if (!(await session.platform.confirm(message, 'Clean up skin numbers'))) return;
    const r = await tab.safely(() => session.rpc.call('mods.forgetSkins', { keys: chosen }));
    if (!r) return;
    tab.info(`Forgot ${chosen.length} skin number(s).`);
    orphans = r.orphans;
    chosen = [];
  }
</script>

<section aria-label="Clean up skin numbers">
  <h3>Clean up skin numbers</h3>
  {#if unreadable}
    <p class="warn">The skin numbers could not be read: {unreadable}</p>
  {:else if orphans && orphans.length === 0}
    <p class="hint">No skin numbers to clean up: every added skin's mod is installed.</p>
  {:else if orphans}
    <p class="hint">These skins' mods are no longer installed. Forget them to let new skins reuse their numbers.</p>
    <ul class="orphans">
      {#each orphans as orphan (orphan.species + '|' + orphan.key)}
        <li>
          <label>
            <input type="checkbox" aria-label="Forget {orphan.key}" checked={chosen.includes(orphan.key)} onchange={(e) => toggle(orphan.key, e.currentTarget.checked)} />
            {orphan.species} · #{orphan.number} · {orphan.key}
          </label>
        </li>
      {/each}
    </ul>
    <button onclick={forget} disabled={!chosen.length || session.busy}>Forget selected</button>
  {/if}
</section>
