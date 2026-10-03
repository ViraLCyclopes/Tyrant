<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import { getSession } from '$lib/stores/session.svelte';
  import ErrorBanner from './ErrorBanner.svelte';
  import JobBar from './JobBar.svelte';

  let { children }: { children: Snippet } = $props();
  const session = getSession();
  const tabs = [
    { href: '/', label: 'Home' },
    { href: '/assets', label: 'Assets' },
    { href: '/mods', label: 'Mods' },
    { href: '/data', label: 'Data' },
  ];
</script>

<div class="shell">
  <nav class="sidebar" aria-label="Main">
    <div class="brand">Tyrant</div>
    {#each tabs as tab (tab.href)}
      <a href={tab.href} aria-current={page.url.pathname === tab.href ? 'page' : undefined}>{tab.label}</a>
    {/each}
  </nav>
  <div class="main">
    <header class="topbar">
      <span class="workspace path">{session.workspace?.dir ?? 'No workspace open'}</span>
      {#if session.workspace?.stale}<span class="badge warn">Game updated: outputs are stale</span>{/if}
      <JobBar />
    </header>
    {#if session.error}
      <ErrorBanner error={session.error} onFix={(fix) => session.applyFix(fix)} onDismiss={() => (session.error = null)} />
    {/if}
    {#if session.notice}
      <div class="banner info" role="status">
        <span class="text">{session.notice}</span>
        <button class="ghost" aria-label="Dismiss" onclick={() => (session.notice = null)}>✕</button>
      </div>
    {/if}
    <main class="content">{@render children()}</main>
  </div>
</div>

<style>
  .shell { display: grid; grid-template-columns: 180px 1fr; height: 100vh; }
  .sidebar { background: var(--panel); border-right: 1px solid var(--border); padding: 16px 10px; display: flex; flex-direction: column; gap: 4px; }
  .brand { font-weight: 700; font-size: 18px; padding: 0 10px 14px; color: var(--accent); letter-spacing: 0.02em; }
  .sidebar a { color: var(--text); text-decoration: none; padding: 7px 10px; border-radius: 6px; }
  .sidebar a:hover { background: var(--panel-2); }
  .sidebar a[aria-current='page'] { background: var(--accent); color: var(--accent-text); }
  .main { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
  .topbar { display: flex; align-items: center; gap: 12px; padding: 10px 16px; border-bottom: 1px solid var(--border); background: var(--panel); min-height: 48px; }
  .workspace { color: var(--muted); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .content { flex: 1; overflow: auto; padding: 20px 24px; }
</style>
