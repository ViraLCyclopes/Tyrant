import { fireEvent, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { AssetPreview } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import TexturePreview from './TexturePreview.svelte';

const preview: AssetPreview = {
  kind: 'texture', files: ['D:\\ws\\cache\\previews\\b\\x\\texture.png'], width: 2048, height: 1024, format: 'DXT5', mipCount: 12,
  vertices: null, triangles: null, skinned: null, message: null,
};

describe('TexturePreview', () => {
  it('shows the texture from the cache and switches channels', async () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(TexturePreview, session, { preview });

    const image = screen.getByRole('img', { name: 'Texture preview' });
    expect(image).toHaveAttribute('src', 'asset://D:\\ws\\cache\\previews\\b\\x\\texture.png');
    expect(screen.getByText('2048 × 1024 · DXT5 · 12 mip levels')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Alpha' }));

    expect(image).toHaveClass('channel-a');
    expect(screen.getByRole('button', { name: 'Alpha' })).toHaveAttribute('aria-pressed', 'true');
  });
});
