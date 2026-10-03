<script lang="ts">
  import { Registry } from '$lib/shell/registry';
  import Shell from '$lib/shell/Shell.svelte';
  import { ShellState } from '$lib/shell/shellState.svelte';
  import { registerTools } from '$lib/shell/tools';
  import { getSession } from '$lib/stores/session.svelte';

  const session = getSession();
  const registry = new Registry();
  registerTools(registry);
  const shell = new ShellState(registry, session.log, session.store);
  shell.start();
</script>

<Shell {shell} />
