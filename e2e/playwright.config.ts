import { defineConfig, devices } from '@playwright/test';

const FRONTEND_URL = process.env.FRONTEND_URL ?? 'http://localhost:5173';
const BACKEND_URL = process.env.BACKEND_URL ?? 'http://localhost:5080';

/**
 * Every spec here drives TWO browser contexts - one per role (Runner and Hunter) - against the
 * real backend and frontend. Per research.md §4 this is the only layer that exercises the real
 * wire path, which is what the fog-of-war filtering (FR-011) and the SC-006 latency budget
 * actually depend on.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false, // matches share a backend; parallel rooms would race on role assignment
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  // These drive real matches through real servers - a match takes seconds of wall clock,
  // not milliseconds, so Playwright's 30s default is far too tight.
  timeout: 180_000,
  expect: { timeout: 20_000 },
  reporter: process.env.CI ? [['github'], ['html']] : [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: FRONTEND_URL,
    trace: 'on-first-retry',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  webServer: [
    {
      command: 'dotnet run --project ../backend/src/MatchServer --urls ' + BACKEND_URL,
      url: BACKEND_URL + '/health',
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'npm run dev --prefix ../frontend',
      url: FRONTEND_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
