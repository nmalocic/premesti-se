import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  use: { baseURL: process.env.APP_URL ?? 'http://localhost:8080' },
});
