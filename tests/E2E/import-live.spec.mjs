import { test, expect } from '../../src/Web/node_modules/@playwright/test/index.mjs'

test('import preview and cancellation are separate actions', async ({ page }) => {
  await page.route('**/api/v1/auth/session', route => route.fulfill({ json: { authenticated: true, demo: false, csrf: '' } }))
  let job = null
  let previews = 0
  let starts = 0
  let cancels = 0
  await page.route('**/api/v1/data-status?*', async route => {
    const upstream = await route.fetch()
    const status = await upstream.json()
    await route.fulfill({ json: { ...status, job, progress: job ? { percent: 12, remainingWindows: 7, seconds: null } : null } })
  })
  await page.route(/\/api\/v1\/import-jobs(?:\/.*)?$/, async route => {
    const { pathname } = new URL(route.request().url())
    if (pathname.endsWith('/estimate')) {
      previews++
      return route.fulfill({ json: { windows: 8, seconds: 120 } })
    }
    if (route.request().method() === 'DELETE') {
      cancels++
      job = { ...job, status: 'cancelled', cancelled: '2026-10-01T00:30:00Z' }
      return route.fulfill({ status: 204 })
    }
    starts++
    job = { id: '1', status: 'running', start: '2026-10-01T00:00:00Z', end: '2026-10-02T00:00:00Z',
      checkpoint: '2026-10-01T03:00:00Z', windows: 1, emptyWindows: 1, readingRows: 0, aggregateRows: 0 }
    await route.fulfill({ status: 202, json: { id: '1' } })
  })
  await page.goto('/?page=status&kind=day&anchor=2026-10-01')
  await page.getByLabel('Importbeginn').fill('2026-10-01')
  await page.getByLabel('Importende exklusiv').fill('2026-10-02')
  await page.getByRole('button', { name: 'Dauer schätzen' }).click()
  await expect(page.getByText(/8 Zeitfenster · Basisdauer ca. 2 Min/)).toBeVisible()
  expect(previews).toBe(1)
  expect(starts).toBe(0)
  await page.getByRole('button', { name: 'Import einplanen' }).click()
  await expect(page.getByRole('progressbar')).toHaveAttribute('value', '12')
  await expect(page.getByText(/Restzeit wird ermittelt/)).toBeVisible()
  await expect(page.getByText(/davon 1 leer/)).toBeVisible()
  await page.getByRole('button', { name: 'Import abbrechen' }).click()
  await expect(page.getByText('Abgebrochen', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Dauer schätzen' })).toBeVisible()
  expect(starts).toBe(1)
  expect(cancels).toBe(1)
})

test('overview reads cache and signals visibility for its own tab', async ({ page }) => {
  await page.route('**/api/v1/auth/session', route => route.fulfill({ json: { authenticated: true, demo: false, csrf: '' } }))
  const signals = []
  await page.route('**/api/v1/live/activity', route => {
    signals.push(route.request().postDataJSON())
    return route.fulfill({ status: 204 })
  })
  const missing = { value: null, direction: 'unknown', measuredAt: null, ageSeconds: null, quality: 'missing' }
  await page.route('**/api/v1/live/now', route => route.fulfill({ json: {
    pv: missing, load: missing, battery: missing, soc: missing, pvSurplusKw: null, flowConfirmed: false,
    grid: { value: -1.2, direction: 'export', measuredAt: '2026-10-01T12:00:00Z', ageSeconds: 20, quality: 'confirmed' }
  } }))
  await page.route('**/api/v1/live/today', route => route.fulfill({ json: {
    start: '2026-10-01T00:00:00Z', end: '2026-10-02T00:00:00Z', gaps: [],
    samples: [{ channel: 'grid', at: '2026-10-01T12:00:00Z', value: -1.2, source: 'live' }]
  } }))
  await page.goto('/?kind=day&anchor=2026-10-01')
  await expect(page.getByText('Einspeisung · 1,2 kW')).toBeVisible()
  await expect(page.getByRole('img', { name: 'Heutiger Leistungsverlauf' })).toBeVisible()
  await expect.poll(() => signals.at(-1)?.visible).toBe(true)
  await page.getByRole('button', { name: 'Datenstatus' }).click()
  await expect.poll(() => signals.at(-1)?.visible).toBe(false)
  expect(signals.at(-1).tab).toBe(signals[0].tab)
})

test('Demo renders the shared live dashboard and synthetic probe', async ({ page, request }) => {
  const now = await (await request.get('/api/v1/live/now')).json()
  expect(now.quality).toBe('synthetic')
  for (const channel of ['pv', 'load', 'grid', 'battery', 'soc']) expect(typeof now[channel].value).toBe('number')
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Jetzt', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Heute', exact: true })).toBeVisible()
  await expect(page.getByRole('img', { name: 'Heutiger Leistungsverlauf' })).toBeVisible()
  await page.getByRole('button', { name: 'Einstellungen', exact: true }).click()
  await expect(page.getByLabel('Hausverbrauch · Live-ID')).toHaveValue('13119')
  await page.getByRole('button', { name: 'Live-Probe abrufen' }).click()
  await expect(page.getByText(/PV-Erzeugung .* kW · Hausverbrauch .* kW/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Live-Zuordnung speichern' })).toBeDisabled()
})
