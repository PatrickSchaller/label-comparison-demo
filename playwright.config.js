import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/browser',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: { baseURL: 'http://127.0.0.1:5098', browserName: 'chromium', trace: 'retain-on-failure' },
  webServer: {
    command: 'dotnet run --project src/LabelReview -c Release --no-build --no-launch-profile',
    url: 'http://127.0.0.1:5098/api/status',
    reuseExistingServer: !process.env.CI,
    env: { OPENAI_API_KEY: '', OPENAI_MODEL: '' }
  }
});
