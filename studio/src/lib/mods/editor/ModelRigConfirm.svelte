<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModModelRigPreviewDto, ModRigBone } from '$lib/rpc/types.gen';
  import { boneParts } from './rigText';

  /** The rig edit found in a model about to be added: where it comes from, its bones, and (if the mod has one already) which to keep. */
  let { preview, action, onConfirm, onCancel }: {
    preview: ModModelRigPreviewDto;
    action: 'Add' | 'Replace';
    onConfirm: (rig: 'auto' | 'useFile' | 'keepCurrent') => void;
    onCancel: () => void;
  } = $props();

  const uid = $props.id();
  // svelte-ignore state_referenced_locally
  let choice = $state<'useFile' | 'keepCurrent'>(preview.question === 'keepOrRemove' ? 'keepCurrent' : 'useFile');
  let confirmButton: HTMLButtonElement;

  const source = $derived(
    preview.source === 'bones' ? "From the model's bones"
    : preview.source === 'rigFile' ? `From ${(preview.rigFile ?? '').split(/[\\/]/).pop()}`
    : preview.source === 'saved' ? 'Same as the current rig edit'
    : "The model has the game's skeleton",
  );
  const count = $derived(preview.bones.length);

  function confirm() {
    onConfirm(preview.question === 'none' ? 'auto' : choice);
  }

  onMount(() => confirmButton?.focus());
</script>

<svelte:window onkeydown={(e) => e.key === 'Escape' && onCancel()} />

<div class="confirm" role="group" aria-label="Rig edit">
  <p><strong>{source}</strong></p>
  {#snippet bones(list: ModRigBone[], label: string)}
    {#if list.length > 8}
      <details>
        <summary>{label}: {list.length} bones</summary>
        {@render items(list)}
      </details>
    {:else}
      <p>{label}: {list.length} {list.length === 1 ? 'bone' : 'bones'}</p>
      {@render items(list)}
    {/if}
  {/snippet}
  {#snippet items(list: ModRigBone[])}
    <ul>
      {#each list as b (b.bone)}
        <li><strong>{b.bone}</strong>{#each boneParts(b) as p (p)} <span>{p}</span>{/each}</li>
      {/each}
    </ul>
  {/snippet}
  {#if count > 0}
    {#if count > 8}
      <details>
        <summary>{count} bones</summary>
        {@render items(preview.bones)}
      </details>
    {:else}
      <p>{count} {count === 1 ? 'bone' : 'bones'}</p>
      {@render items(preview.bones)}
    {/if}
  {:else}
    <p class="hint">No rig edit.</p>
  {/if}
  {#if preview.question === 'keepOrRemove' && preview.current?.length}
    {@render bones(preview.current, 'The current rig edit')}
  {/if}

  {#if preview.question === 'useOrKeep'}
    <fieldset>
      <legend>Which rig edit?</legend>
      <label><input type="radio" name="{uid}-rig" value="useFile" bind:group={choice} /> Use the model's rig edit</label>
      <label><input type="radio" name="{uid}-rig" value="keepCurrent" bind:group={choice} /> Keep the current one ({preview.current?.length ?? 0} bones)</label>
    </fieldset>
  {:else if preview.question === 'keepOrRemove'}
    <fieldset>
      <legend>The model has no rig edit</legend>
      <label><input type="radio" name="{uid}-rig" value="keepCurrent" bind:group={choice} /> Keep the current rig edit</label>
      <label><input type="radio" name="{uid}-rig" value="useFile" bind:group={choice} /> Remove it</label>
    </fieldset>
  {/if}

  {#each preview.errors as e (e)}<p class="warn" role="alert">{e}</p>{/each}
  {#each preview.warnings as w (w)}<p class="hint">{w}</p>{/each}

  <div class="row">
    <button class="primary" bind:this={confirmButton} disabled={preview.errors.length > 0} onclick={confirm}>{action}</button>
    <button onclick={onCancel}>Cancel</button>
  </div>
</div>

<style>
  .confirm { margin: 8px 0; padding: 8px; border: 1px solid var(--border, #8884); border-radius: 6px; }
  ul { margin: 4px 0; padding-left: 18px; }
  li span { margin-left: 8px; }
  fieldset { border: 0; margin: 8px 0; padding: 0; display: grid; gap: 4px; }
  .row { display: flex; gap: 8px; margin: 8px 0 0; }
</style>
