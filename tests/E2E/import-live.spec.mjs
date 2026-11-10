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
  await page.clock.install()
  await page.route('**/api/v1/auth/session', route => route.fulfill({ json: { authenticated: true, demo: false, csrf: '' } }))
  const signals = []
  await page.route('**/api/v1/live/activity', route => {
    signals.push(route.request().postDataJSON())
    return route.fulfill({ status: 204 })
  })
  const missing = { value: null, direction: 'unknown', measuredAt: null, ageSeconds: null, quality: 'missing' }
  let gridValue = -1.2
  await page.route('**/api/v1/live/now', route => route.fulfill({ json: {
    pv: { ...missing, quality: 'unconfigured' }, load: missing, battery: missing,
    pvDc: { ...missing, value: .84, measuredAt: '2026-10-01T12:00:00Z', quality: 'confirmed' },
    inverterActive: { ...missing, value: -.16, measuredAt: '2026-10-01T12:00:00Z', quality: 'confirmed' },
    soc: { ...missing, value: 0, quality: 'confirmed' }, pvSurplusKw: null, flowConfirmed: false,
    quality: 'repeated', fetchedAt: '2026-10-01T12:05:00Z',
    timing: { overviewVisible: true, cloudIntervalSeconds: 300, sourceMeasuredAt: '2026-10-01T12:00:00Z', nextEligibleAt: null, retryAt: null },
    grid: { value: gridValue, direction: 'export', measuredAt: '2026-10-01T12:00:00Z', ageSeconds: 20, quality: 'confirmed' }
  } }))
  await page.route('**/api/v1/live/today', route => route.fulfill({ json: {
    start: '2026-10-01T00:00:00Z', end: '2026-10-02T00:00:00Z', gaps: [],
    samples: [{ channel: 'grid', at: '2026-10-01T12:00:00Z', value: -1.2, source: 'live' }]
  } }))
  await page.goto('/?kind=day&anchor=2026-10-01')
  await expect(page.getByText('1,2 kW', { exact: true })).toBeVisible()
  await expect(page.getByText('Leer · 0 %')).toBeVisible()
  await expect(page.getByText('PV-DC-Leistung am Wechselrichter', { exact: true })).toBeVisible()
  await expect(page.getByText('AC-PV-Fluss nicht separat gemessen')).toBeVisible()
  await expect(page.getByText('Wechselrichter-Wirkleistung', { exact: true })).toBeVisible()
  await page.getByText('Messalter und Abrufstatus', { exact: true }).click()
  await expect(page.getByText(/keinen neueren Messwert/)).toBeVisible()
  gridValue = -1.5
  await page.clock.fastForward(30_000)
  await expect(page.getByText('1,5 kW', { exact: true })).toBeVisible()
  await page.getByText('Heutigen Leistungsverlauf anzeigen').click()
  await expect(page.getByRole('img', { name: 'Heutiger Leistungsverlauf' })).toBeVisible()
  await expect.poll(() => signals.at(-1)?.visible).toBe(true)
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    document.dispatchEvent(new Event('visibilitychange'))
  })
  await expect.poll(() => signals.at(-1)?.visible).toBe(false)
  gridValue = -2
  await page.clock.fastForward(30_000)
  await expect(page.getByText('1,5 kW', { exact: true })).toBeVisible()
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
  await expect(page.getByRole('img', { name: 'Heutiger Leistungsverlauf' })).toHaveCount(0)
  await page.getByText('Heutigen Leistungsverlauf anzeigen').click()
  await expect(page.getByRole('img', { name: 'Heutiger Leistungsverlauf' })).toBeVisible()
  await page.getByRole('button', { name: 'Einstellungen', exact: true }).click()
  await page.getByText('Live-Diagnose mit Demodaten', { exact: true }).click()
  await expect(page.getByText('Hausverbrauch · 13119', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Live-Probe abrufen' }).click()
  await expect(page.getByText(/PV-Erzeugung .* kW · Hausverbrauch .* kW/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Live-Zuordnung speichern' })).toHaveCount(0)
})

test('owner can inspect live candidates and a targeted device history window', async ({ page }) => {
  const device = '42_14_1_1'
  const time = '2026-11-09T11:00:00Z'
  await page.route('**/api/v1/auth/session', route => route.fulfill({ json: { authenticated: true, demo: false, csrf: '' } }))
  await page.route('**/api/v1/provider/status', route => route.fulfill({ json: {
    configured: true, authorized: true, defaultZone: 'Europe/Berlin',
    activeMapping: { id: '42', name: 'Testanlage', zone: 'Europe/Berlin',
      mapping: { deviceCounters: true, stepMinutes: 15, live: { deviceKey: device, confirmed: true } } },
  } }))
  await page.route('**/api/v1/live/mapping', route => route.fulfill({
    json: { mapping: null, devices: [{ key: device, type: 'Speicherwechselrichter', model: 'Testgerät', candidateProbeSupported: true }] },
  }))
  await page.route('**/api/v1/provider/probe/live-candidates', route => {
    expect(route.request().postDataJSON()).toEqual({ device })
    return route.fulfill({ json: { source: 'Geräte-Livewerte', fetchedAt: time, points: [
      { point: 13141, channel: 'Ladezustand', unit: '%', delivered: true, validCount: 1, status: 'available',
        values: [{ measuredAt: time, value: 0 }] },
      { point: 13003, channel: 'DC-Gesamtleistung', delivered: false, validCount: 0, status: 'missing', values: [] },
    ] } })
  })
  await page.route('**/api/v1/provider/probe/device-minutes', route => {
    expect(route.request().postDataJSON()).toEqual({ device, day: '2026-11-09', start: '11:00:00', end: '13:00:00' })
    return route.fulfill({ json: {
      source: 'Gerätehistorie · 15 Minuten', fetchedAt: time, failure: 'authorization', resultCode: 'E900', points: [],
    } })
  })
  const backfillRequests = []
  await page.route('**/api/v1/provider/device-counters/backfill*', route => {
    const apply = new URL(route.request().url()).searchParams.get('apply') === 'true'
    backfillRequests.push(apply)
    expect(route.request().postDataJSON()).toEqual({ device, day: '2026-11-09', start: '11:00:00', end: '13:00:00', stepMinutes: 15 })
    return route.fulfill({ json: { candidates: [{}], written: apply ? 1 : 0 } })
  })
  await page.goto('/?page=settings')
  await page.getByText('Diagnose und Messpunkte', { exact: true }).click()
  const form = page.locator('form').filter({ has: page.getByRole('heading', { name: 'Gerätemesspunkte lesend prüfen' }) })
  await form.getByRole('button', { name: 'Gerätemesspunkte abrufen' }).click()
  await expect(form.getByText(/· 0 %/)).toBeVisible()
  await expect(form.getByText(/Ursache ungeklärt/)).toBeVisible()
  await form.getByLabel('Quelle').selectOption('history')
  await form.getByLabel('Abgeschlossener Tag').fill('2026-11-09')
  await form.getByRole('button', { name: 'Gerätemesspunkte abrufen' }).click()
  await expect(form.getByText(/Lesezugriff nicht autorisiert.*E900/)).toBeVisible()
  const backfill = page.locator('form').filter({ has: page.getByRole('heading', { name: 'Zählerdaten nachladen' }) })
  await backfill.getByLabel('Abgeschlossener Tag').fill('2026-11-09')
  await backfill.getByRole('button', { name: 'Nachimport prüfen' }).click()
  await expect(backfill.getByText(/1 Intervalle im gewählten Fenster/)).toBeVisible()
  expect(backfillRequests).toEqual([false])
  await backfill.getByRole('button', { name: 'Zählerintervalle übernehmen' }).click()
  await expect(backfill.getByText(/1 Intervalle neu gespeichert/)).toBeVisible()
  expect(backfillRequests).toEqual([false, true])
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

test('Sungrow daily totals stay separate from incomplete interval charts', async ({ page }) => {
  await page.route('**/api/v1/overview?*', async route => {
    const response = await (await route.fetch()).json()
    const unavailable = { kwh: null, coverage: 0 }
    const monthly = new URL(route.request().url()).searchParams.get('kind') === 'month'
    const full = kwh => ({ kwh, coverage: monthly ? .2 : 1 })
    const { metrics } = response.report
    await route.fulfill({ json: {
      ...response,
      report: { ...response.report, resolutionMinutes: 15, warnings: ['Teildaten. Energiemengen beziehen sich auf erfasste Intervalle.'],
        metrics: { ...metrics, coverage: 0, pv: unavailable, load: full(22.5), import: unavailable, export: unavailable, autarky: null, retention: null },
        providerMetrics: { ...metrics, coverage: monthly ? .2 : 1, pv: full(16.6), load: full(22.5), import: full(6.5), export: full(0), autarky: null, retention: null } },
      buckets: [{ start: '2026-11-06T10:00:00Z', local: '11:00', load: 1 }],
    } })
  })
  await page.route('**/api/v1/heatmap?*', route => route.fulfill({ json: { points: [
    { day: '2026-11-06', hour: 11, offset: '01:00', import: null, export: 0 },
  ] } }))
  await page.route('**/api/v1/energy-insights?*', async route => {
    const data = await (await route.fetch()).json()
    const single = data.period.days === 1
    await route.fulfill({ json: { ...data, knownPaths: false, latest: '2026-11-06T23:00:00Z',
      breakdown: { household: null, production: null, validHours: 0, missingHours: 24, rejectedHours: 0 },
      points: single ? Array.from({ length: 96 }, (_, i) => ({
        start: new Date(Date.parse('2026-11-05T23:00:00Z') + i * 900000).toISOString(),
        end: new Date(Date.parse('2026-11-05T23:00:00Z') + (i + 1) * 900000).toISOString(),
        importKw: null, exportKw: null, soc: null, directKw: null, mix: null, limit: null,
      })) : [] } })
  })
  await page.goto('/?kind=day&anchor=2026-11-06')
  const kpis = page.locator('.kpis')
  await expect(kpis.getByText(/Gemeinsame .*abdeckung/)).toHaveCount(0)
  await expect(page.getByText('Achtung: Der Zeitverlauf ist unvollständig')).toBeVisible()
  await expect(kpis.locator('article').filter({ hasText: 'Autarkiegrad' })).toContainText('71,1')
  await expect(page.getByText(/Keine Werte im Verlauf für PV-Erzeugung/)).toBeVisible()
  const balance = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Energiebilanz im gewählten Zeitraum' }) })
  await expect(balance).toContainText('100 %')
  await expect(balance).toContainText('0 von 24 Stunden')
  await expect(page.getByText(/Keine Minutenmesswerte für Netzbezug, Einspeisung, Ladezustand/)).toBeVisible()
  await expect(page.getByRole('img', { name: 'Gemeldeter SOC und Netzleistung auf lokaler Uhrzeitachse' })).toHaveCount(0)
  const info = page.getByLabel('Details zu Autarkiegrad')
  await info.focus()
  await info.press('Enter')
  await expect(kpis.getByText(/Näherung aus Tageswerten · setzt Betrieb ohne Netzladen voraus/)).toBeVisible()
  await page.getByText('Hinweise zur Datengrundlage').click()
  await expect(page.getByText(/Gemeinsame Minutenabdeckung: 0 % der Zeit/)).toBeVisible()
  await expect(page.getByText(/Gemeinsame Tagesabdeckung: 100 % der Tage/)).toBeVisible()
  await expect(page.getByText(/Im Minutenverlauf fehlen gemeinsame Messintervalle/)).toBeVisible()
  const heatmap = page.getByRole('combobox', { name: 'Heatmap', exact: true })
  await heatmap.selectOption('import')
  await expect(page.getByText('Keine Stundenwerte für Netzbezug im gewählten Zeitraum.')).toBeVisible()
  await heatmap.selectOption('export')
  await expect(page.getByRole('img', { name: 'Heatmap nach Tag, Stunde und UTC-Abstand' })).toBeVisible()
  await page.goto('/?kind=month&anchor=2026-11-06')
  await page.getByLabel('Details zu Autarkiegrad').click()
  await expect(kpis.locator('article').filter({ hasText: 'Autarkiegrad' }))
    .toContainText('Zu wenig gemeinsame Messdaten für den gewählten Zeitraum')
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

test('energy indicators wrap at 375 pixels with larger text', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 })
  await page.goto('/')
  await expect(page.getByText('Eigenverbrauchsquote', { exact: true })).toBeVisible()
  await page.evaluate(() => { document.documentElement.style.fontSize = '125%' })
  const widths = await page.locator('.kpis article').evaluateAll(cards => cards.map(card =>
    card.scrollWidth <= card.clientWidth))
  expect(widths).toHaveLength(7)
  expect(widths.every(Boolean)).toBe(true)
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(375)
})
