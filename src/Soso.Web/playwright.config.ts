import { defineConfig } from '@playwright/test';

export default defineConfig( {
    testDir: './e2e',
    fullyParallel: true,
    use: { baseURL: 'http://127.0.0.1:5199', viewport: { width: 1366, height: 900 }, trace: 'retain-on-failure' },
    webServer: { command: 'npm run dev -- --port 5199 --strictPort', url: 'http://127.0.0.1:5199', reuseExistingServer: false },
} );