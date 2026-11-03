import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    passWithNoTests: false,
    reporters: ['default', 'junit'],
    outputFile: { junit: 'test-results/unit/junit.xml' },
    restoreMocks: true,
    unstubGlobals: true,
  },
});
