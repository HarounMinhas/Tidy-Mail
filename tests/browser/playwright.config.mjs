import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.mjs',
  timeout: 90_000,
  expect: { timeout: 20_000 },
  retries: 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:5088',
    browserName: 'chromium',
    headless: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  }
});
