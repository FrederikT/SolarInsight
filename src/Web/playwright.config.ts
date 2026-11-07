import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: '../../tests/E2E',
  testMatch: '**/*.spec.mjs',
  timeout: 75_000,
  workers: 1,
  use: { locale: 'de-DE', baseURL: process.env.SOLAR_E2E_URL ?? 'http://127.0.0.1:8080', viewport: { width: 1400, height: 1080 } },
})

