import { defineConfig, devices } from '@playwright/test'

// Ports pinned to avoid the ASPNETCORE default-port ambiguity (5000 vs. the
// 5240 this repo's docs/scripts assume) — see CLAUDE.md's `dotnet run` line.
const SERVER_URL = 'http://localhost:5240'
const WEB_URL = 'http://localhost:5173'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  use: {
    baseURL: WEB_URL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../src/Project27.Server',
      url: `${SERVER_URL}/api/version`,
      env: { ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: SERVER_URL },
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: 'npm run dev',
      url: WEB_URL,
      env: { P27_SERVER: SERVER_URL },
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
  ],
})
