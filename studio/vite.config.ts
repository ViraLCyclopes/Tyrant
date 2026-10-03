import { sveltekit } from '@sveltejs/kit/vite';
import { svelteTesting } from '@testing-library/svelte/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [sveltekit(), svelteTesting()],
  // Tauri expects a fixed dev port, and its own output must stay visible in the terminal.
  clearScreen: false,
  server: { port: 1420, strictPort: true, watch: { ignored: ['**/src-tauri/**'] } },
  test: {
    environment: 'jsdom',
    setupFiles: ['src/vitest-setup.ts'],
    include: ['src/**/*.test.ts'],
  },
});
