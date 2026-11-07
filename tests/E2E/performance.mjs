import assert from 'node:assert/strict'
import os from 'node:os'
import { performance } from 'node:perf_hooks'
const base = process.env.SOLAR_E2E_URL ?? 'http://127.0.0.1:8080'
const cookies = new Map()
let csrf = ''
async function query(path, options = {}) {
  const response = await fetch(base + '/api/v1' + path, {
    ...options,
    headers: { ...options.headers, 'X-CSRF': csrf,
      Cookie: [...cookies.entries()].map(([key, value]) => key + '=' + value).join('; ') },
  })
  for (const header of response.headers.getSetCookie()) {
    const cookie = header.split(';')[0]
    const index = cookie.indexOf('=')
    cookies.set(cookie.slice(0, index), cookie.slice(index + 1))
  }
  const payload = await response.json()
  assert.ok(response.ok, JSON.stringify(payload))
  return payload
}

async function sample(path) {
  await query(path)
  const values = []
  for (let i = 0; i < 25; i++) {
    const start = performance.now()
    await query(path)
    values.push(performance.now() - start)
  }
  return Math.round(values.sort((a, b) => a - b)[Math.ceil(values.length * .95) - 1])
}

async function simulate(profile) {
  const t = performance.now()
  const { id } = await query('/simulations', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(profile),
  })
  while (performance.now() - t < 65_000) {
    const job = await query('/simulations/' + id)
    if (job.state === 'completed') return { ms: Math.round(performance.now() - t), warnings: job.result.warnings }
    assert.ok(job.state !== 'failed', job.error)
    await new Promise(resolve => setTimeout(resolve, 250))
  }
  throw new Error('Zeitlimit überschritten.')
}

const session = await query('/auth/session')
assert.equal(session.demo, true)
csrf = session.csrf
const monthMs = await sample('/overview?kind=month&anchor=2026-10-15')
const dayMs = await sample('/timeseries?kind=day&anchor=2026-10-25')
const battery = { capacity: 15, chargeKw: 4, dischargeKw: 4, chargeEfficiency: .94,
  dischargeEfficiency: .94, minSoc: .1, maxSoc: 1 }
const period = { start: '2025-01-01', end: '2026-01-01' }
const year = await simulate({ period, battery })
const combined = await simulate({ period, battery,
  shift: { daily: 1, share: .2, targetKw: 1, source: { from: 0, to: 1440 }, target: { from: 540, to: 1020 } },
  added: { daily: 2, kw: 2, days: [1, 3, 5], full: false, window: { from: 540, to: 1020 } } })
console.log(JSON.stringify({ cpu: os.cpus()[0]?.model, logicalCores: os.cpus().length,
  memoryGiB: Math.round(os.totalmem() / 1024 ** 3), monthP95Ms: monthMs, dayP95Ms: dayMs, year, combined }, null, 2))
assert.ok(monthMs < 2000 && dayMs < 2000 && year.ms < 5000 && combined.ms < 60_000)

