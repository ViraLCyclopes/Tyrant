<script lang="ts">
  import { Registry } from '$lib/shell/registry';
  import Shell from '$lib/shell/Shell.svelte';
  import { ShellState } from '$lib/shell/shellState.svelte';
  import { registerTools } from '$lib/shell/tools';
  import { getSession } from '$lib/stores/session.svelte';

  const session = getSession();
  const registry = new Registry();
  registerTools(registry);
  // A job whose tab was closed can still fail: its error then shows above every tab.
  const shell = new ShellState(registry, session.log, session.store, (error) => (session.error = error));
  shell.start();
</script>

<Shell {shell} />
