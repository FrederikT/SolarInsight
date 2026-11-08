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
  await page.getByText('Nachimport und Testimport', { exact: true }).click()
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
  await page.getByText('Nachimport und Testimport', { exact: true }).click()
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
  await page.getByText('Live-Diagnose mit Demodaten', { exact: true }).click()
  await expect(page.getByText('Hausverbrauch · 13119', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Live-Probe abrufen' }).click()
  await expect(page.getByText(/PV-Erzeugung .* kW · Hausverbrauch .* kW/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Live-Zuordnung speichern' })).toHaveCount(0)
})

test('mobile owner setup requires only consent and plant selection', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/v1/auth/session', route => route.fulfill({ json: { authenticated: true, demo: false, csrf: '' } }))
  let authorized = false
  let activeMapping = null
  await page.route('**/api/v1/provider/status', route => route.fulfill({ json: {
    configured: true, authorized, defaultZone: 'Europe/Berlin', activeMapping
  } }))
  await page.route('**/api/v1/provider/authorize', async route => {
    expect(route.request().postDataJSON()).toEqual({ code: 'fixture-code' })
    authorized = true
    await route.fulfill({ status: 204 })
  })
  await page.route('**/api/v1/provider/plants?*', route => route.fulfill({ json: [{ id: '42', name: 'Testanlage' }] }))
  await page.route('**/api/v1/provider/plant', async route => {
    expect(route.request().postDataJSON()).toEqual({ plant: '42', zone: 'Europe/Berlin', page: 1 })
    activeMapping = { id: '42', name: 'Testanlage', zone: 'Europe/Berlin',
      mapping: { daily: { pv: 83022, load: 83118, import: 83102, export: 83072 } } }
    await route.fulfill({ status: 204 })
  })
  await page.route('**/api/v1/live/mapping', route => route.fulfill({ json: { mapping: null, devices: [] } }))
  await page.route('**/api/v1/data-status?*', async route => {
    const status = await (await route.fetch()).json()
    await route.fulfill({ json: { ...status, progress: null, lastSuccess: '2026-10-01T10:00:00Z',
      job: { ...status.job, status: 'failed', error: 'auth' } } })
  })
  await page.goto('/?page=settings')
  await page.getByLabel('Autorisierungscode').fill('fixture-code')
  await page.getByRole('button', { name: 'Autorisieren', exact: true }).click()
  await expect(page.getByLabel('Anlagenzeitzone')).toHaveValue('Europe/Berlin')
  await page.getByRole('button', { name: 'Anlage aktivieren' }).click()
  await expect(page.getByText('Anlage verbunden', { exact: true })).toBeVisible()
  await page.getByText('Diagnose und Messpunkte', { exact: true }).click()
  await expect(page.getByText('PV-Erzeugung · Kein bestätigter Minutenmesspunkt für diese Anlage.')).toBeVisible()
  await expect(page.getByText('PV-Erzeugung · 83022')).toBeVisible()
  await expect(page.getByLabel(/Messpunkt-ID|Live-ID|Zeitfenster/)).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)

  await page.getByRole('button', { name: 'Datenstatus' }).click()
  await expect(page.getByText('Letzter erfolgreicher Datenabruf')).toBeVisible()
  await expect(page.getByText(/Autorisiere die Anlage in den Einstellungen erneut/)).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375)
})

test('all main views fit on desktop and mobile', async ({ page }) => {
  for (const width of [1400, 375]) {
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/')
    for (const [tab, heading] of [
      ['Übersicht', 'Übersicht'], ['Speicher & Potenzial', 'Speicher & Potenzial'],
      ['Datenstatus', 'Datenstatus'], ['Einstellungen', 'Einstellungen'],
    ]) {
      await page.getByRole('button', { name: tab, exact: true }).click()
      await expect(page.getByRole('heading', { name: heading, level: 1, exact: true })).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth), tab + ' at ' + width).toBeLessThanOrEqual(width)
    }
  }
})
