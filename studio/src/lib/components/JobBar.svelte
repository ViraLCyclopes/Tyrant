<script lang="ts">
  import { getSession } from '$lib/stores/session.svelte';

  const session = getSession();
</script>

{#if session.job}
  <div class="job" aria-live="polite">
    <span class="title">{session.job.title}</span>
    <progress max="1" value={session.job.fraction} aria-label="{session.job.title} progress"></progress>
    <span class="message">{session.job.message}</span>
    <button class="ghost" disabled={!session.job.cancel} onclick={() => session.cancelJob()}>Cancel</button>
  </div>
{/if}

<style>
  .job { display: flex; align-items: center; gap: 10px; min-width: 0; }
  .title { font-weight: 600; }
  progress { width: 160px; }
  .message { color: var(--muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 360px; }
</style>
