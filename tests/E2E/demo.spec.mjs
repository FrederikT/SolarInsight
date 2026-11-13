import { test, expect } from '../../src/Web/node_modules/@playwright/test/index.mjs'
import { readFile } from 'node:fs/promises'

test('A20/A23 Demo: Zeitraum, Tagesdetail, Speicher, kombinierter Lauf und Exporte', async ({ page, request }) => {
  await expect.poll(async () => {
    const response = await request.get('/api/v1/data-status?kind=month')
    if (!response.ok()) return null
    return (await response.json()).job?.status
  }, { timeout: 60000 }).toBe('completed')
  await page.goto('/')
  await expect(page.getByText('Synthetische Demodaten', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Übersicht', exact: true })).toBeVisible()
  await page.getByText('Hinweise zur Datengrundlage', { exact: false }).click()
  await expect(page.getByText(/^Gemeinsame Minutenabdeckung: 100 %/)).toBeVisible()
  if (process.env.SOLAR_SCREENSHOT) {
    await expect(page.getByRole('img', { name: 'Verlauf im Zeitraum' })).toBeVisible()
    await page.screenshot({ path: process.env.SOLAR_SCREENSHOT, fullPage: true })
  }
  await page.getByRole('combobox', { name: 'Zeitraum', exact: true }).selectOption('week')
  await page.getByRole('combobox', { name: 'Kalenderwoche', exact: true }).selectOption({ label: 'KW 42' })
  await page.getByText('Diagrammdaten als Tabelle').click()
  await page.getByRole('table').getByRole('button').first().click()
  await expect(page.getByRole('heading', { name: 'Tagesdetail', exact: true })).toBeVisible()
  await expect(page.getByRole('img', { name: 'Tagesverlauf mit Leistung und Ladezustand' })).toBeVisible()
  await page.getByRole('button', { name: 'Zurück zum Zeitraum' }).click()
  await expect(page.getByRole('combobox', { name: 'Zeitraum', exact: true })).toHaveValue('week')
  await page.getByRole('heading', { name: 'Beobachtungen im Zeitraum' }).locator('..').getByRole('button').first().click()
  await expect(page.getByRole('heading', { name: /Tagesdetail|Ereigniszeitraum/ })).toBeVisible()
  await page.getByRole('button', { name: 'Zurück zum Zeitraum' }).click()
  const downloadCsv = page.waitForEvent('download')
  await page.getByRole('link', { name: 'CSV exportieren' }).click()
  const csv = await downloadCsv
  expect(await readFile(await csv.path(), 'utf8')).toContain('PV_kWh')
  await page.getByRole('button', { name: 'Speicher & Potenzial', exact: true }).click()
  await expect(page.getByRole('img', { name: 'Gemessene Einspeisung nach lokaler Stunde' })).toBeVisible()
  await expect(page.getByRole('columnheader', { name: 'Im Ladefenster nutzbar (kWh)', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Was bringt ein größerer Speicher?' })).toBeVisible()
  await expect(page.getByRole('img', { name: 'Vermiedener Netzbezug nach Zusatzkapazität' })).toBeVisible()
  await page.locator('summary', { hasText: 'Detaillierte Intervallsimulation' }).click()
  await page.getByLabel('Speicher ändern').check()
  await page.getByLabel('Kapazität vor Reserve (kWh)').fill('16')
  await page.getByRole('button', { name: 'Simulation starten' }).click()
  await expect(page.getByRole('heading', { name: 'Simulationsergebnis' })).toBeVisible({ timeout: 25000 })
  await page.getByLabel('Verbrauch verschieben').check()
  await page.getByLabel('Zusatzverbrauch ergänzen').check()
  await page.getByRole('button', { name: 'Simulation starten' }).click()
  await expect(page.getByRole('heading', { name: 'Zusatzbedarf und Energieherkunft' })).toBeVisible({ timeout: 40000 })
  const downloadJson = page.waitForEvent('download')
  await page.getByRole('link', { name: 'JSON exportieren' }).click()
  const json = JSON.parse(await readFile(await (await downloadJson).path(), 'utf8'))
  expect(json.parameters.battery.capacity).toBe(16)
  expect(json.parameters.shift.daily).toBe(1)
  expect(json.parameters.added.daily).toBe(2)
  await page.getByText('Einzelne Tagesabweichungen').click()
  await page.locator('details').filter({ hasText: 'Einzelne Tagesabweichungen' })
    .getByRole('table').getByRole('button').first().click()
  await page.getByLabel('Szenario einblenden').check()
  await expect(page.getByRole('heading', { name: 'Tagesdetail', exact: true })).toBeVisible()
  await page.setViewportSize({ width: 375, height: 800 })
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(375)
})

test('A21 Demo sperrt globale Änderungen und trennt Besucher', async ({ playwright, baseURL }) => {
  const a = await playwright.request.newContext({ baseURL })
  const b = await playwright.request.newContext({ baseURL })
  try {
    const { csrf } = await (await a.get('/api/v1/auth/session')).json()
    const headers = { 'X-CSRF': csrf }
    const tariff = { kind: 0, start: '2026-11-01', end: null, price: .25 }
    expect((await a.post('/api/v1/tariffs', { data: tariff, headers })).status()).toBe(403)
    expect((await a.post('/api/v1/import-jobs', { headers, data: { start: '2026-10-01', end: '2026-10-02' } })).status()).toBe(403)
    expect((await a.put('/api/v1/auth/password', { data: { current: 'x', next: 'y' }, headers })).status()).toBe(401)
    expect((await a.get('/api/v1/provider/status')).status()).toBe(403)
    const created = await a.post('/api/v1/simulations', { headers, data: { period: { start: '2026-10-01', end: '2026-10-08' } } })
    expect(created.status()).toBe(202)
    const { id } = await created.json()
    expect((await b.get('/api/v1/simulations/' + id)).status()).toBe(404)
    expect((await a.get('/api/v1/simulations/' + id)).status()).toBe(200)
    expect((await b.get('/api/v1/exports/simulations/' + id)).status()).toBe(404)
    const bCsrf = (await (await b.get('/api/v1/auth/session')).json()).csrf
    expect((await b.delete('/api/v1/simulations/' + id, { headers: { 'X-CSRF': bCsrf } })).status()).toBe(404)
    expect((await a.delete('/api/v1/simulations/' + id, { headers })).status()).toBe(204)
    await expect.poll(async () => {
      const response = await a.get('/api/v1/simulations/' + id)
      return response.status() === 404 || (await response.json()).state === 'cancelled'
    }).toBe(true)
  } finally { await a.dispose(); await b.dispose() }
})

test('A22 Lokale Anmeldung, CSRF, Anlagen- und Tarifpflege', async ({ playwright }) => {
  const baseURL = process.env.SOLAR_PRIVATE_URL
  const password = process.env.SOLAR_TEST_PASSWORD_FILE ?
    (await readFile(process.env.SOLAR_TEST_PASSWORD_FILE, 'utf8')).trim() : undefined
  test.skip(!baseURL || !password, 'Separater privater Testserver erforderlich.')
  const client = await playwright.request.newContext({ baseURL })
  try {
    expect((await client.get('/api/v1/plant')).status()).toBe(401)
    expect((await client.post('/api/v1/auth/login', { data: { password } })).status()).toBe(400)
    const { csrf } = await (await client.get('/api/v1/auth/session')).json()
    expect((await client.post('/api/v1/auth/login', { data: { password }, headers: { 'X-CSRF': csrf } })).status()).toBe(204)
    expect((await client.get('/api/v1/plant')).status()).toBe(200)
    const headers = { 'X-CSRF': (await (await client.get('/api/v1/auth/session')).json()).csrf }
    const submittedMapping = await client.put('/api/v1/provider/plant', {
      headers, data: { plant: '42', mapping: { pv: 83067 } }
    })
    expect(submittedMapping.status()).toBe(400)
    const liveMappingWrite = await client.put('/api/v1/live/mapping', { headers, data: { pv: 13003 } })
    expect([404, 405]).toContain(liveMappingWrite.status())
    const plant = await client.put('/api/v1/settings/plant', { headers, data: { name: 'Backupfixture',
      zone: 'Europe/Berlin', since: '2026-01-01', pvKwp: 12, battery: null,
      rule: { kw: .1, socTolerance: .02, minutes: 15 } } })
    expect(plant.status()).toBe(204)
    const tariff = { kind: 0, start: '2026-01-01', end: null, price: .3 }
    const rows = await (await client.get('/api/v1/tariffs')).json()
    const previous = rows.find(row => row.start === tariff.start && row.kind === tariff.kind)
    const saved = previous ? await client.put('/api/v1/tariffs/' + previous.id, { headers, data: tariff }) :
      await client.post('/api/v1/tariffs', { headers, data: tariff })
    expect(saved.status()).toBe(200)
    const conflict = await client.post('/api/v1/tariffs', { headers, data: { ...tariff, price: .4 } })
    expect(conflict.status()).toBe(400)
    const response = await client.get('/api/v1/settings/plant')
    expect(await response.text()).not.toContain(password)
    const csv = await client.get('/api/v1/exports/overview?kind=month')
    expect(await csv.text()).not.toContain(password)
    expect((await client.post('/api/v1/auth/logout', { headers })).status()).toBe(204)
    expect((await client.get('/api/v1/plant')).status()).toBe(401)
  } finally { await client.dispose() }
})

