import { render, waitFor } from '@testing-library/svelte';
import { flushSync } from 'svelte';
import { describe, expect, it } from 'vitest';
import { TAB_KEY } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { SESSION_KEY, Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { testTab } from '$lib/test/fixtures';
import Thumb from './Thumb.svelte';

describe('Thumb', () => {
  it('asks again for a file replaced under the same name', async () => {
    let n = 0;
    const rpc = new FakeRpc().on('mods.thumbnail', () => ({ file: `D:\\ws\\cache\\previews\\mods\\t${++n}.png` }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const props = $state({ modId: 'red-spot', file: 'skins/blue/male_D.png', label: 'male diffuse', version: 'r1' });
    render(Thumb, { props, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, testTab(session)]]) });
    await waitFor(() => expect(rpc.callsTo('mods.thumbnail')).toHaveLength(1));

    props.version = 'r2'; // same file name, new content
    flushSync();

    await waitFor(() => expect(rpc.callsTo('mods.thumbnail')).toHaveLength(2));
  });
});
