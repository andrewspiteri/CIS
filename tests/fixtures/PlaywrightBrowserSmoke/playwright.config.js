const { defineConfig } = require('playwright/test');

module.exports = defineConfig({
  testDir: __dirname,
  testMatch: 'browser.spec.js',
  workers: 1,
  retries: 0,
  timeout: 15_000,
  expect: { timeout: 3_000 },
  reporter: 'line',
  outputDir: 'test-results',
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
  use: {
    headless: true,
    trace: 'off',
    video: 'off',
    screenshot: 'off',
    launchOptions: process.env.CIS_BROWSER_SMOKE_EXECUTABLE_PATH
      ? { executablePath: process.env.CIS_BROWSER_SMOKE_EXECUTABLE_PATH }
      : {},
  },
});
