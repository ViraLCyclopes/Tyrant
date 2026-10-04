import { render, waitFor } from '@testing-library/svelte';
import { flushSync } from 'svelte';
import { describe, expect, it } from 'vitest';
import { TAB_KEY, Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { SESSION_KEY, Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { modDetail } from '$lib/test/modFixtures';
import ColourPreviewStrip from './ColourPreviewStrip.svelte';
import { ModDoc } from './modDoc.svelte';

describe('ColourPreviewStrip', () => {
  it('redraws for another skin, even when neither has colours', async () => {
    const rpc = new FakeRpc().on('mods.get', () => modDetail()).on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }));
    rpc.on('mods.colorPreview', () => ({ files: [] }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    const doc = new ModDoc('red-spot', rpc, tab, () => {});
    await doc.load();
    const [blue, red] = doc.detail!.skins;
    const props = $state({ doc, skin: blue, colorsJson: null as string | null, variant: 'normal' as const });
    render(ColourPreviewStrip, { props, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, tab]]) });
    await waitFor(() => expect(rpc.callsTo('mods.colorPreview').at(-1)?.params).toMatchObject({ skin: 'blue' }));

    props.skin = red; // the skin page is reused when another skin is picked
    flushSync();

    await waitFor(() => expect(rpc.callsTo('mods.colorPreview').at(-1)?.params).toMatchObject({ skin: 'red' }));
  });
});
