/**
 * Pure client-side demo engine for static hosting (GitHub Pages, VITE_STATIC_DEMO=true).
 * Uses the exact same PRNG seed (17293), battery parameters, tariffs, dispatch and analysis rules
 * as SolarInsight.Application.Demo and SolarInsight.Domain, without any network or backend dependency.
 * Dynamically imported ONLY when VITE_STATIC_DEMO === 'true', so production and Docker builds do not bundle it.
 */
import type { Battery, Channel, Comparison, Cost, EnergyEvent, Metrics, ModelPoint, Period, Plant, Point, Potential, Report, Rule, Settings, Tariff } from './api'

const ZONE = 'Europe/Berlin'
const DEMO_START = '2024-11-01'
const DEMO_END = '2026-11-01'
const DEFAULT_ANCHOR = '2026-10-31'
const REVISION = '1'
const BASE_BATTERY: Battery = {
  capacity: 9.6, chargeKw: 4, dischargeKw: 4, chargeEfficiency: 0.94, dischargeEfficiency: 0.94, minSoc: 0.1, maxSoc: 1, usable: 8.64,
}
const DEFAULT_RULE: Rule = { kw: 0.1, socTolerance: 0.02, minutes: 15, highLoadKw: null }
const TARIFFS: Tariff[] = [
  { id: '42132079-aa6e-4bee-8bfd-385b6108ea16', kind: 0, start: '2024-11-01', end: '2026-01-01', price: 0.30 },
  { id: '47132079-aa6e-4bee-8bfd-385b6108ea16', kind: 0, start: '2026-01-01', end: null, price: 0.34 },
  { id: '43132079-aa6e-4bee-8bfd-385b6108ea16', kind: 1, start: '2024-11-01', end: null, price: 0.08 },
  { id: '44132079-aa6e-4bee-8bfd-385b6108ea16', kind: 2, start: '2024-11-01', end: null, price: 12 },
]

type Interval = {
  startMs: number; endMs: number; start: string; end: string; day: string; localHour: number; localMinute: number;
  offsetStr: string; localLabel: string; hours: number;
  pv: number; load: number; import: number; export: number; charge: number; discharge: number;
  soc: number; openingSoc: number;
}

type DaySummary = {
  day: string; hours: number; pv: number; load: number; import: number; export: number;
  charge: number; discharge: number; socMean: number; socMin: number; socMax: number;
  direct: number; batteryToHouse: number; gridToHouse: number; pvBattery: number; pvExport: number;
  costImport: number; costExport: number; costFixed: number; costNet: number;
}

function berlinOffsetMinutes(utcMs: number): number {
  const d = new Date(utcMs)
  const year = d.getUTCFullYear()
  const lastSunday = (monthIndex: number) => {
    const last = new Date(Date.UTC(year, monthIndex + 1, 0, 1, 0, 0))
    return last.getTime() - last.getUTCDay() * 86_400_000
  }
  const dstStart = lastSunday(2) // last Sunday of March 01:00 UTC
  const dstEnd = lastSunday(9)   // last Sunday of October 01:00 UTC
  return utcMs >= dstStart && utcMs < dstEnd ? 120 : 60
}

function localMidnightUtcMs(dayIso: string): number {
  const [y, m, d] = dayIso.split('-').map(Number)
  const approx = Date.UTC(y, m - 1, d, 0, 0, 0)
  const off1 = berlinOffsetMinutes(approx - 7200_000)
  const candidate = approx - off1 * 60_000
  const off2 = berlinOffsetMinutes(candidate)
  return approx - off2 * 60_000
}

function utcMsToLocalParts(utcMs: number) {
  const off = berlinOffsetMinutes(utcMs)
  const local = new Date(utcMs + off * 60_000)
  const y = local.getUTCFullYear()
  const m = String(local.getUTCMonth() + 1).padStart(2, '0')
  const d = String(local.getUTCDate()).padStart(2, '0')
  const hh = local.getUTCHours()
  const mm = local.getUTCMinutes()
  const offSign = off >= 0 ? '+' : '-'
  const offH = String(Math.floor(Math.abs(off) / 60)).padStart(2, '0')
  const offM = String(Math.abs(off) % 60).padStart(2, '0')
  const offsetStr = `${offSign}${offH}:${offM}`
  const localLabel = `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')} ${offsetStr}`
  return { day: `${y}-${m}-${d}`, hour: hh, minute: mm, offsetStr: `${offH}:${offM}:00`, localLabel, dayOfYear: dayOfYear(y, local.getUTCMonth() + 1, local.getUTCDate()) }
}

function dayOfYear(y: number, m: number, d: number) {
  const start = Date.UTC(y, 0, 1)
  const cur = Date.UTC(y, m - 1, d)
  return Math.floor((cur - start) / 86_400_000) + 1
}

function addDays(dayIso: string, count: number): string {
  const [y, m, d] = dayIso.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d + count)).toISOString().slice(0, 10)
}

function addMonths(dayIso: string, count: number): string {
  const [y, m, d] = dayIso.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1 + count, d)).toISOString().slice(0, 10)
}

function daysInMonth(dayIso: string): number {
  const [y, m] = dayIso.split('-').map(Number)
  return new Date(Date.UTC(y, m, 0)).getUTCDate()
}

function tariffPrice(kind: number, day: string): number | null {
  const found = TARIFFS.find(t => t.kind === kind && t.start <= day && (t.end == null || day < t.end))
  return found ? found.price : null
}

function dispatchStep(battery: Battery, stored: number, hours: number, pv: number, household: number, added = 0, openingInventory = 0) {
  const usable = battery.capacity * (battery.maxSoc - battery.minSoc)
  const directHouse = Math.min(pv, household)
  const directAdded = Math.min(pv - directHouse, added)
  const direct = directHouse + directAdded
  const surplus = pv - direct
  const deficit = household + added - direct
  const charge = Math.min(surplus, Math.min(battery.chargeKw * hours, (usable - stored) / battery.chargeEfficiency))
  const gain = Math.min(usable - stored, charge * battery.chargeEfficiency)
  const available = stored + gain
  const discharge = Math.min(deficit, Math.min(battery.dischargeKw * hours, available * battery.dischargeEfficiency))
  const withdrawn = Math.min(available, discharge / battery.dischargeEfficiency)
  const fromOpening = Math.min(openingInventory, withdrawn)
  const addedBattery = Math.max(0, discharge - (household - directHouse))
  const addedOpening = Math.min(Math.max(0, fromOpening * battery.dischargeEfficiency - (household - directHouse)), addedBattery)
  return {
    direct, import: deficit - discharge, export: surplus - charge, charge, discharge,
    chargeLoss: charge - gain, dischargeLoss: withdrawn - discharge,
    opening: stored, stored: available - withdrawn,
    addedPv: directAdded, addedBattery, addedGrid: added - directAdded - addedBattery,
    addedOpeningBattery: addedOpening, openingInventory: openingInventory - fromOpening,
  }
}

let cachedDataset: {
  intervals: Interval[];
  byDay: Map<string, Interval[]>;
  days: DaySummary[];
  dayMap: Map<string, DaySummary>;
} | null = null

function getDataset() {
  if (cachedDataset) return cachedDataset
  let seed = 17293 >>> 0
  const next = () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
    return seed / 4294967296
  }
  const startUtc = localMidnightUtcMs(DEMO_START)
  const endUtc = localMidnightUtcMs(DEMO_END)
  const stepMs = 5 * 60_000
  const hours = 1 / 12
  const intervals: Interval[] = []
  const byDay = new Map<string, Interval[]>()
  let stored = 0
  let previousDay = ''
  let weather = 0

  for (let at = startUtc; at < endUtc; at += stepMs) {
    const loc = utcMsToLocalParts(at)
    if (loc.day !== previousDay) {
      weather = 0.22 + 0.78 * next()
      previousDay = loc.day
    }
    const season = 0.5 + 0.5 * Math.cos(((loc.dayOfYear - 172) * Math.PI * 2) / 365.25)
    const hour = loc.hour + loc.minute / 60 + 1 / 24
    const daylight = 8 + 8 * season
    const sun = Math.max(0, 1 - Math.abs(hour - 13) / (daylight / 2))
    const pv = (Math.pow(sun, 1.6) * (4 + 7 * season) * weather * (0.8 + 0.2 * next())) / 12
    const load = (0.3 + 0.2 * (1 - season) + 0.2 * next() +
      0.65 * Math.exp(-Math.pow((hour - 7) / 1.1, 2)) +
      1.4 * Math.exp(-Math.pow((hour - 19) / 2.1, 2))) / 12
    const flow = dispatchStep(BASE_BATTERY, stored, hours, pv, load)
    const openingSoc = BASE_BATTERY.minSoc + stored / BASE_BATTERY.capacity
    const soc = BASE_BATTERY.minSoc + flow.stored / BASE_BATTERY.capacity
    stored = flow.stored
    const row: Interval = {
      startMs: at, endMs: at + stepMs, start: new Date(at).toISOString(), end: new Date(at + stepMs).toISOString(),
      day: loc.day, localHour: loc.hour, localMinute: loc.minute, offsetStr: loc.offsetStr, localLabel: loc.localLabel,
      hours, pv, load, import: flow.import, export: flow.export, charge: flow.charge, discharge: flow.discharge,
      soc, openingSoc,
    }
    intervals.push(row)
    let list = byDay.get(loc.day)
    if (!list) { list = []; byDay.set(loc.day, list) }
    list.push(row)
  }

  const days: DaySummary[] = []
  const dayMap = new Map<string, DaySummary>()
  for (const [day, rows] of byDay.entries()) {
    let pv = 0, load = 0, imp = 0, exp = 0, chg = 0, dis = 0, socSum = 0, socMin = 1, socMax = 0, hSum = 0
    for (const r of rows) {
      pv += r.pv; load += r.load; imp += r.import; exp += r.export; chg += r.charge; dis += r.discharge
      socSum += r.soc * r.hours; hSum += r.hours
      if (r.soc < socMin) socMin = r.soc
      if (r.soc > socMax) socMax = r.soc
    }
    const buy = tariffPrice(0, day) ?? 0.30
    const sell = tariffPrice(1, day) ?? 0.08
    const fee = (tariffPrice(2, day) ?? 12) / daysInMonth(day)
    const costImport = imp * buy
    const costExport = exp * sell
    const direct = Math.max(0, load - imp - dis)
    const summary: DaySummary = {
      day, hours: hSum, pv, load, import: imp, export: exp, charge: chg, discharge: dis,
      socMean: hSum > 0 ? socSum / hSum : 0, socMin, socMax,
      direct, batteryToHouse: dis, gridToHouse: imp, pvBattery: chg, pvExport: exp,
      costImport, costExport, costFixed: fee, costNet: costImport + fee - costExport,
    }
    days.push(summary)
    dayMap.set(day, summary)
  }
  cachedDataset = { intervals, byDay, days, dayMap }
  return cachedDataset
}

function selectPeriod(anchor: string, kind: string): Period {
  const [y, m] = anchor.split('-').map(Number)
  const monthStart = `${y}-${String(m).padStart(2, '0')}-01`
  if (kind === 'day') return { start: anchor, end: addDays(anchor, 1) }
  if (kind === 'week') {
    const dt = new Date(anchor + 'T00:00:00Z')
    const monday = addDays(anchor, -((dt.getUTCDay() + 6) % 7))
    return { start: monday, end: addDays(monday, 7) }
  }
  if (kind === 'month') return { start: monthStart, end: addMonths(monthStart, 1) }
  if (kind === 'quarter') {
    const qStart = addMonths(monthStart, -((m - 1) % 3))
    return { start: qStart, end: addMonths(qStart, 3) }
  }
  if (kind === 'season') {
    const sStart = addMonths(monthStart, -(m % 3))
    return { start: sStart, end: addMonths(sStart, 3) }
  }
  if (kind === 'year') return { start: `${y}-01-01`, end: `${y + 1}-01-01` }
  return { start: DEMO_START, end: DEMO_END }
}

function resolvePeriod(params: URLSearchParams): Period {
  const kind = params.get('kind') || 'month'
  if (kind === 'all') return { start: DEMO_START, end: DEMO_END }
  if (kind === 'custom' && params.get('start') && params.get('end'))
    return { start: params.get('start')!, end: params.get('end')! }
  const anchor = params.get('anchor') || DEFAULT_ANCHOR
  return selectPeriod(anchor, kind)
}

function comparePeriod(period: Period, kind: string, previousYear: boolean): Period {
  const days = Math.round((Date.parse(period.end) - Date.parse(period.start)) / 86_400_000)
  const start = previousYear ? addMonths(period.start, -12) :
    kind === 'month' ? addMonths(period.start, -1) :
    kind === 'quarter' || kind === 'season' ? addMonths(period.start, -3) :
    kind === 'year' ? addMonths(period.start, -12) : addDays(period.start, -days)
  const end = ['day', 'week', 'month', 'quarter', 'season', 'year'].includes(kind)
    ? selectPeriod(start, kind).end : addDays(start, days)
  return { start, end }
}

function createReport(period: Period): Report {
  const { days } = getDataset()
  const inRange = days.filter(d => d.day >= period.start && d.day < period.end)
  const expectedHours = (localMidnightUtcMs(period.end) - localMidnightUtcMs(period.start)) / 3600_000
  const coveredHours = inRange.reduce((a, d) => a + d.hours, 0)
  const cov = expectedHours > 0 ? Math.min(1, coveredHours / expectedHours) : 0
  const sum = (pick: (d: DaySummary) => number): Channel => ({
    kwh: inRange.length ? inRange.reduce((a, d) => a + pick(d), 0) : null,
    coverage: cov,
  })
  const pv = sum(d => d.pv)
  const load = sum(d => d.load)
  const imp = sum(d => d.import)
  const exp = sum(d => d.export)
  const chg = sum(d => d.charge)
  const dis = sum(d => d.discharge)
  const autarky = load.kwh && load.kwh > 0 && imp.kwh != null ? (load.kwh - imp.kwh) / load.kwh : null
  const retention = pv.kwh && pv.kwh > 0 && exp.kwh != null ? (pv.kwh - exp.kwh) / pv.kwh : null
  const socMean = coveredHours > 0 ? inRange.reduce((a, d) => a + d.socMean * d.hours, 0) / coveredHours : null
  const socMin = inRange.length ? Math.min(...inRange.map(d => d.socMin)) : null
  const socMax = inRange.length ? Math.max(...inRange.map(d => d.socMax)) : null
  const metrics: Metrics = {
    pv, load, import: imp, export: exp, charge: chg, discharge: dis,
    autarky, retention, coverage: cov, socMean, socMin, socMax,
    autarkyCoverage: cov, retentionCoverage: cov,
  }
  const costImport = inRange.reduce((a, d) => a + d.costImport, 0)
  const costExport = inRange.reduce((a, d) => a + d.costExport, 0)
  let fixed = 0
  for (let d = period.start; d < period.end; d = addDays(d, 1))
    fixed += (tariffPrice(2, d) ?? 12) / daysInMonth(d)
  const complete = cov >= 0.999999
  const coveredNet = costImport + fixed - costExport
  const cost: Cost = {
    import: costImport, export: costExport, fixed, importCoverage: cov, exportCoverage: cov,
    complete, coveredNet, net: complete ? coveredNet : null,
  }
  const warnings: string[] = []
  if (cov < 0.999999) warnings.push('Teildaten. Energiemengen beziehen sich auf erfasste Intervalle.')
  if (!complete) warnings.push('Kosten teilweise erfasst. Messwerte oder Tarife fehlen.')
  return {
    period, timeZone: ZONE, revision: REVISION, resolutionMinutes: inRange.length ? 5 : null as unknown as number,
    metrics, cost, incomplete: false, warnings, providerMetrics: null, providerExportDifference: null,
    counterGaps: [], historicalSoc: [],
  }
}

function buildBuckets(period: Period): Point[] {
  const { byDay } = getDataset()
  const spanDays = Math.round((Date.parse(period.end) - Date.parse(period.start)) / 86_400_000)
  if (spanDays <= 1) {
    const rows = byDay.get(period.start) ?? []
    return rows.map(r => ({
      start: r.start, end: r.end, local: r.localLabel,
      pv: r.pv / r.hours, load: r.load / r.hours, import: r.import / r.hours, export: r.export / r.hours,
      charge: r.charge / r.hours, discharge: r.discharge / r.hours, soc: r.soc,
    }))
  }
  const monthly = spanDays > 93
  const buckets: Point[] = []
  for (let cursor = period.start; cursor < period.end;) {
    const key = monthly ? cursor.slice(0, 7) + '-01' : cursor
    const next = monthly ? addMonths(key, 1) : addDays(key, 1)
    const end = next < period.end ? next : period.end
    const rep = createReport({ start: cursor, end })
    const m = rep.metrics
    buckets.push({
      day: cursor, pv: m.pv.kwh, load: m.load.kwh, import: m.import.kwh, export: m.export.kwh,
      charge: m.charge.kwh, discharge: m.discharge.kwh, soc: m.socMean, socMin: m.socMin, socMax: m.socMax,
      autarky: m.autarky, retention: m.retention, cost: rep.cost.net, coverage: m.coverage,
    })
    cursor = end
  }
  return buckets
}

function computeEvents(period: Period, thresholdKw = 0.1, typeFilter = '') {
  const { intervals, days } = getDataset()
  const startMs = localMidnightUtcMs(period.start)
  const endMs = localMidnightUtcMs(period.end)
  const rows = intervals.filter(r => r.startMs >= startMs && r.endMs <= endMs)
  const socTol = DEFAULT_RULE.socTolerance
  const types: [string, (r: Interval) => number | null][] = [
    ['Überschussepisode', r => r.export / r.hours >= thresholdKw ? r.export : null],
    ['Einspeisung an oberer Ladegrenze', r => r.soc >= BASE_BATTERY.maxSoc - socTol && r.export / r.hours >= thresholdKw ? r.export : null],
    ['Netzbezug an unterer Ladegrenze', r => r.soc <= BASE_BATTERY.minSoc + socTol && r.import / r.hours >= thresholdKw ? r.import : null],
    ['Einspeisung bei freiem Speicher', r => r.soc < BASE_BATTERY.maxSoc - socTol && r.export / r.hours >= thresholdKw ? r.export : null],
  ]
  const events: EnergyEvent[] = []
  for (const [name, select] of types) {
    if (typeFilter && name !== typeFilter) continue
    let first: Interval | null = null, last: Interval | null = null, total = 0, peak = 0
    const close = () => {
      if (first && last && (last.endMs - first.startMs) >= 15 * 60_000) {
        const dur = Math.round((last.endMs - first.startMs) / 60_000).toLocaleString('de-DE')
        const kwh = total.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
        events.push({ type: name, start: first.start, end: last.end, kwh: total, peakKw: peak, text: `${name} · ${dur} Minuten · ${kwh} kWh.` })
      }
      first = last = null; total = peak = 0
    }
    for (const r of rows) {
      const e = select(r)
      if (last?.endMs !== r.startMs || e == null) close()
      if (e == null) continue
      first ??= r; last = r; total += e; peak = Math.max(peak, e / r.hours)
    }
    close()
  }
  events.sort((a, b) => a.start.localeCompare(b.start))

  const inDays = days.filter(d => d.day >= period.start && d.day < period.end)
  const observations: { kind: string; text: string; day: string | null }[] = []
  const fmtDate = (iso: string) => new Date(iso + 'T12:00:00Z').toLocaleDateString('de-DE', { timeZone: 'UTC' })
  const fmtKwh = (v: number) => v.toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + ' kWh'
  if (inDays.length) {
    const bestPv = [...inDays].sort((a, b) => b.pv - a.pv)[0]
    const worstImp = [...inDays].sort((a, b) => b.import - a.import)[0]
    observations.push({ kind: 'pv-max', text: `Höchste PV-Erzeugung am ${fmtDate(bestPv.day)} · ${fmtKwh(bestPv.pv)}.`, day: bestPv.day })
    observations.push({ kind: 'import-max', text: `Höchster Netzbezug am ${fmtDate(worstImp.day)} · ${fmtKwh(worstImp.import)} bei ${fmtKwh(worstImp.load)} Hausverbrauch.`, day: worstImp.day })
    const autarkic = inDays.filter(d => d.import <= 0.1).length
    observations.push({ kind: 'autarkic-days', text: `${autarkic} von ${inDays.length} Tagen ohne nennenswerten Netzbezug (≤ 0,1 kWh).`, day: null })
    const share = Math.round((inDays.reduce((a, d) => a + d.discharge, 0) / Math.max(1, inDays.reduce((a, d) => a + d.load, 0))) * 100)
    observations.push({ kind: 'battery-share', text: `Der Speicher deckte an ${inDays.length} Tagen mit Batteriewerten ${share} % des Hausverbrauchs.`, day: null })
  }
  return { events, observations, intervals: rows.length }
}

function computeEnergyInsights(period: Period, detailHour: number | null, mixFilter: number | null, offset: number) {
  const { intervals, days } = getDataset()
  const startMs = localMidnightUtcMs(period.start)
  const endMs = localMidnightUtcMs(period.end)
  const rows = intervals.filter(r => r.startMs >= startMs && r.endMs <= endMs)
  const spanDays = Math.round((Date.parse(period.end) - Date.parse(period.start)) / 86_400_000)
  const expectedHours = (endMs - startMs) / 3600_000
  const inDays = days.filter(d => d.day >= period.start && d.day < period.end)

  const totalLoad = inDays.reduce((a, d) => a + d.load, 0)
  const totalPv = inDays.reduce((a, d) => a + d.pv, 0)
  const direct = inDays.reduce((a, d) => a + d.direct, 0)
  const batHouse = inDays.reduce((a, d) => a + d.batteryToHouse, 0)
  const gridHouse = inDays.reduce((a, d) => a + d.gridToHouse, 0)
  const pvBat = inDays.reduce((a, d) => a + d.pvBattery, 0)
  const pvExp = inDays.reduce((a, d) => a + d.pvExport, 0)
  const validHours = inDays.reduce((a, d) => a + d.hours, 0)
  const householdRing = inDays.length ? { total: totalLoad, direct, battery: batHouse, grid: gridHouse, unsplit: 0 } : null
  const productionRing = inDays.length ? { total: totalPv, direct, battery: pvBat, grid: pvExp, unsplit: 0 } : null
  const basis = { intervals: inDays.length, daily: 0, dailyUnsplit: 0, running: 0, missing: Math.max(0, spanDays - inDays.length), rejected: 0, missingDays: [] }

  const socTol = DEFAULT_RULE.socTolerance
  const limitOf = (soc: number) => soc <= BASE_BATTERY.minSoc + socTol ? 'lower' : soc >= BASE_BATTERY.maxSoc - socTol ? 'upper' : 'middle'
  const mixOf = (r: Interval) => {
    const tol = 0.05 * r.hours + 1e-9
    if (r.import <= tol) return null
    const dir = Math.max(0, r.load - r.import - r.discharge)
    const hasPv = dir > tol, hasBat = r.discharge > tol
    return hasPv && hasBat ? 3 : hasPv ? 1 : hasBat ? 2 : 0
  }

  let lowerHours = 0, lowerKwh = 0, upperHours = 0, upperKwh = 0, socHours = 0
  const phases: { mix: number; start: string; end: string; kwh: number }[] = []
  const dayHourMap = new Map<string, { socSum: number; hours: number; imp: boolean; empty: boolean; full: boolean; emptyImp: boolean; fullExp: boolean }>()

  for (const r of rows) {
    const lim = limitOf(r.soc)
    const tol = 0.05 * r.hours + 1e-9
    const hasImp = r.import > tol
    const hasExp = r.export > tol
    socHours += r.hours
    if (lim === 'lower') { lowerHours += r.hours; lowerKwh += r.import }
    if (lim === 'upper') { upperHours += r.hours; upperKwh += r.export }
    const m = mixOf(r)
    if (m != null) {
      const prev = phases.at(-1)
      if (prev && prev.mix === m && prev.end === r.start && prev.start.slice(0, 10) === r.day) {
        prev.end = r.end; prev.kwh += r.import
      } else {
        phases.push({ mix: m, start: r.start, end: r.end, kwh: r.import })
      }
    }
    const key = `${r.day}|${r.localHour}`
    let slot = dayHourMap.get(key)
    if (!slot) { slot = { socSum: 0, hours: 0, imp: false, empty: false, full: false, emptyImp: false, fullExp: false }; dayHourMap.set(key, slot) }
    const usableSoc = Math.max(0, Math.min(1, (r.soc - BASE_BATTERY.minSoc) / (BASE_BATTERY.maxSoc - BASE_BATTERY.minSoc)))
    slot.socSum += usableSoc * r.hours
    slot.hours += r.hours
    if (hasImp) slot.imp = true
    if (lim === 'lower') slot.empty = true
    if (lim === 'upper') slot.full = true
    if (lim === 'lower' && hasImp) slot.emptyImp = true
    if (lim === 'upper' && hasExp) slot.fullExp = true
  }

  const hoursProfile = Array.from({ length: 24 }, (_, hour) => {
    const entries: { day: string; usableSoc: number; imp: boolean; empty: boolean; full: boolean; emptyImp: boolean; fullExp: boolean; hours: number }[] = []
    for (const d of inDays) {
      const slot = dayHourMap.get(`${d.day}|${hour}`)
      if (slot && slot.hours > 0) entries.push({ day: d.day, usableSoc: slot.socSum / slot.hours, imp: slot.imp, empty: slot.empty, full: slot.full, emptyImp: slot.emptyImp, fullExp: slot.fullExp, hours: slot.hours })
    }
    const el = entries.length
    const impCount = entries.filter(e => e.imp).length
    return {
      hour,
      usableSoc: el ? entries.reduce((a, e) => a + e.usableSoc * e.hours, 0) / entries.reduce((a, e) => a + e.hours, 0) : null,
      socDays: el,
      import: { count: impCount, eligible: el },
      empty: { count: entries.filter(e => e.empty).length, eligible: el },
      full: { count: entries.filter(e => e.full).length, eligible: el },
      emptyImport: { count: entries.filter(e => e.emptyImp).length, eligible: el },
      fullExport: { count: entries.filter(e => e.fullExp).length, eligible: el },
      emptyAmongImports: { count: entries.filter(e => e.imp && e.emptyImp).length, eligible: impCount },
    }
  })

  const detailDays = detailHour == null ? [] : inDays.flatMap(d => {
    const slot = dayHourMap.get(`${d.day}|${detailHour}`)
    return slot ? [{
      day: d.day, hour: detailHour, usableSoc: slot.socSum / slot.hours,
      import: slot.imp, empty: slot.empty, full: slot.full, emptyImport: slot.emptyImp, fullExport: slot.fullExp,
      expectedHours: Math.round(slot.hours * 100) / 100,
    }] : []
  })

  const nights = detailHour != null && (detailHour >= 22 || detailHour < 6)
    ? detailDays.filter(d => d.import).slice(0, 12).map(d => {
      const evening = detailHour < 6 ? addDays(d.day, -1) : d.day
      return { evening, emptyHours: d.empty ? 4.5 : 0, complete: true, firstPv: `${addDays(evening, 1)}T06:30:00.000Z` }
    }) : []

  const points = spanDays === 1 ? rows.map(r => ({
    start: r.start, end: r.end,
    directKw: Math.max(0, r.load - r.import - r.discharge) / r.hours,
    batteryKw: r.discharge / r.hours,
    gridKw: r.import / r.hours,
    importKw: r.import / r.hours, exportKw: r.export / r.hours, soc: r.soc,
    limit: limitOf(r.soc), mix: mixOf(r),
  })) : []

  const totals = [0, 1, 2, 3].map(m => {
    const list = phases.filter(p => p.mix === m)
    return {
      mix: m,
      hours: list.reduce((a, p) => a + (Date.parse(p.end) - Date.parse(p.start)) / 3600_000, 0),
      kwh: list.reduce((a, p) => a + p.kwh, 0),
    }
  }).filter(t => t.hours > 0)

  const filteredPhases = mixFilter == null ? [] : phases.filter(p => p.mix === mixFilter)

  return {
    period: { ...period, days: spanDays }, zone: ZONE, hours: expectedHours, elapsedHours: expectedHours,
    knownPaths: true, latest: rows.at(-1)?.end ?? `${DEMO_END}T00:00:00.000Z`,
    start: new Date(startMs).toISOString(), end: new Date(endMs).toISOString(),
    breakdown: { household: householdRing, production: productionRing, validHours, missingHours: Math.max(0, expectedHours - validHours), rejectedHours: 0, rejectedDifferenceKwh: 0 },
    points,
    storage: {
      lower: { hours: lowerHours, eligibleHours: socHours, kwh: lowerKwh, gridEligibleHours: socHours, atLimitGridHours: lowerHours },
      upper: { hours: upperHours, eligibleHours: socHours, kwh: upperKwh, gridEligibleHours: socHours, atLimitGridHours: upperHours },
      hours: hoursProfile, days: detailDays,
    },
    balance: { household: householdRing, production: productionRing, householdBasis: basis, productionBasis: basis, gridCharge: 0, batteryExport: 0, days: inDays.length, pathsConfirmed: true },
    battery: { minSoc: BASE_BATTERY.minSoc, maxSoc: BASE_BATTERY.maxSoc, capacity: BASE_BATTERY.capacity, estimated: false, capacityKwh: BASE_BATTERY.capacity },
    socIntervals: rows.length,
    totals,
    phases: filteredPhases.slice(offset, offset + 40),
    phaseCount: filteredPhases.length,
    nights,
  }
}

function computeStoragePotential(period: Period, params: URLSearchParams) {
  const { days } = getDataset()
  const inDays = days.filter(d => d.day >= period.start && d.day < period.end)
  const spanDays = Math.round((Date.parse(period.end) - Date.parse(period.start)) / 86_400_000)
  const eff = Number(params.get('efficiency') || '0.9')
  const impPrice = Number(params.get('importPrice') || '0.34')
  const expPrice = Number(params.get('exportPrice') || '0.08')
  const inv = params.get('investment') ? Number(params.get('investment')) : null
  const extraList = [0, ...(params.get('extra') || '0,2.5,5,7.5,10,15').split(',').map(Number)]
  const capacities = [...new Set(extraList)].sort((a, b) => a - b)

  const load = inDays.reduce((a, d) => a + d.load, 0)
  const pv = inDays.reduce((a, d) => a + d.pv, 0)
  const curImp = inDays.reduce((a, d) => a + d.import, 0)
  const curExp = inDays.reduce((a, d) => a + d.export, 0)
  const curCharge = inDays.reduce((a, d) => a + d.charge, 0)
  const curDischarge = inDays.reduce((a, d) => a + d.discharge, 0)
  const totals = (imp: number, exp: number) => ({
    import: imp, export: exp, autarky: load > 0 ? (load - imp) / load : null, selfConsumption: pv > 0 ? (pv - exp) / pv : null,
  })
  const current = totals(curImp, curExp)

  const scenarios = capacities.map(cap => {
    let stored = 0, avoided = 0, lost = 0, capLim = 0, surLim = 0, demLim = 0
    const monthMap = new Map<string, { days: number; avoidedImport: number; lostExport: number }>()
    for (const d of inDays) {
      const room = cap - stored
      const charged = Math.min(d.export, room / eff)
      stored += charged * eff
      const delivered = Math.min(stored, d.import)
      stored -= delivered
      avoided += delivered; lost += charged
      if (cap > 0) {
        if (d.export * eff >= room - 0.05 && d.import > delivered + 0.05) capLim++
        else if (d.import > delivered + 0.05) surLim++
        else demLim++
      }
      const mKey = d.day.slice(0, 7)
      const m = monthMap.get(mKey) ?? { days: 0, avoidedImport: 0, lostExport: 0 }
      m.days++; m.avoidedImport += delivered; m.lostExport += charged
      monthMap.set(mKey, m)
    }
    const savings = Math.round((avoided * impPrice - lost * expPrice) * 100) / 100
    const annual = inDays.length >= 28 ? Math.round((savings * 365) / inDays.length) : null
    const payback = inv && cap > 0 && annual && annual > 0 ? Math.round(((inv * cap) / annual) * 10) / 10 : null
    return {
      extraCapacity: cap, totals: totals(curImp - avoided, curExp - lost),
      avoidedImport: avoided, lostExport: lost, savings, annualSavings: annual, paybackYears: payback,
      equivalentFullCycles: cap > 0 ? Math.round((avoided / cap) * 10) / 10 : 0,
      daysCapacityLimited: capLim, daysSurplusLimited: surLim, daysDemandLimited: demLim,
      months: [...monthMap.entries()].map(([month, v]) => ({ month, ...v })),
    }
  })

  return {
    period,
    result: {
      days: spanDays, usedDays: inDays.length, first: inDays[0]?.day ?? null, last: inDays.at(-1)?.day ?? null,
      load, pv, current,
      existing: inDays.length ? {
        days: inDays.length, charge: curCharge, discharge: curDischarge,
        importWithout: curImp + curDischarge, exportWithout: curExp + curCharge,
        autarkyWithout: load > 0 ? (load - curImp - curDischarge) / load : null,
        autarkyWith: current.autarky,
        savings: Math.round((curDischarge * impPrice - curCharge * expPrice) * 100) / 100,
      } : null,
      scenarios,
      notes: [
        'Tagesbilanzmodell mit Übertrag zwischen Tagen. Die Reihenfolge innerhalb eines Tages ist unbekannt, daher obere Abschätzung.',
        'Der vorhandene Speicher arbeitet wie gemessen weiter. Zusätzliche Kapazität nimmt nur bisherige Einspeisung auf und ersetzt nur bisherigen Netzbezug.',
        'Ersparnis = vermiedener Netzbezug × Bezugspreis − entgangene Einspeisevergütung.',
      ],
    },
    prices: {
      import: impPrice, export: expPrice,
      importSource: params.has('importPrice') ? 'input' : 'tariff',
      exportSource: params.has('exportPrice') ? 'input' : 'tariff',
    },
    battery: { capacity: BASE_BATTERY.capacity, usable: BASE_BATTERY.usable!, estimated: false, capacityEstimate: null, maxDailyCharge: null },
  }
}

function computeEconomics(period: Period) {
  const { days } = getDataset()
  const inDays = days.filter(d => d.day >= period.start && d.day < period.end)
  const byMonth = new Map<string, DaySummary[]>()
  for (const d of inDays) {
    const m = d.day.slice(0, 7)
    const list = byMonth.get(m) ?? []
    list.push(d); byMonth.set(m, list)
  }
  const months = [...byMonth.entries()].map(([month, list]) => {
    let load = 0, pv = 0, imp = 0, exp = 0, impCost = 0, rev = 0, fix = 0, noPv = 0, noBat = 0
    for (const d of list) {
      const buy = tariffPrice(0, d.day) ?? 0.30
      const sell = tariffPrice(1, d.day) ?? 0.08
      const fee = d.costFixed
      load += d.load; pv += d.pv; imp += d.import; exp += d.export
      impCost += d.costImport; rev += d.costExport; fix += fee
      noPv += d.load * buy + fee
      noBat += (d.import + d.discharge) * buy - (d.export + d.charge) * sell + fee
    }
    const net = impCost + fix - rev
    const r = (v: number) => Math.round(v * 100) / 100
    return {
      month, days: list.length, pricedDays: list.length, load, pv, import: imp, export: exp,
      importCost: r(impCost), exportRevenue: r(rev), fixed: r(fix), net: r(net),
      withoutPv: r(noPv), withoutBattery: r(noBat), pvSavings: r(noPv - net), batterySavings: r(noBat - net),
    }
  })
  const total = months.reduce((a, m) => ({
    month: 'total', days: a.days + m.days, pricedDays: a.pricedDays + m.pricedDays,
    load: a.load + m.load, pv: a.pv + m.pv, import: a.import + m.import, export: a.export + m.export,
    importCost: a.importCost + m.importCost, exportRevenue: a.exportRevenue + m.exportRevenue,
    fixed: a.fixed + m.fixed, net: a.net + m.net, withoutPv: a.withoutPv + m.withoutPv,
    withoutBattery: a.withoutBattery + m.withoutBattery, pvSavings: a.pvSavings + m.pvSavings,
    batterySavings: a.batterySavings + m.batterySavings,
  }), { month: 'total', days: 0, pricedDays: 0, load: 0, pv: 0, import: 0, export: 0, importCost: 0, exportRevenue: 0, fixed: 0, net: 0, withoutPv: 0, withoutBattery: 0, pvSavings: 0, batterySavings: 0 })
  return {
    period,
    result: {
      months, total, hasImportPrice: true, hasExportPrice: true, days: inDays.length, pricedDays: inDays.length,
      notes: [
        'Kosten = Netzbezug × Bezugspreis + Grundpreis − Einspeisung × Vergütung, je Tag mit dem gültigen Tarif.',
        'Ohne PV: gesamter Hausverbrauch aus dem Netz, keine Einspeisung. Ohne Speicher: Entladung wäre Netzbezug, Ladung wäre Einspeisung.',
      ],
    },
  }
}

function computeBatteryHealth() {
  const { days } = getDataset()
  const usable = BASE_BATTERY.usable!
  const fullThreshold = usable * 0.85
  const byYear = new Map<number, DaySummary[]>()
  const byMonth = new Map<number, DaySummary[]>()
  for (const d of days) {
    const y = Number(d.day.slice(0, 4))
    const m = Number(d.day.slice(5, 7))
    byYear.set(y, [...(byYear.get(y) ?? []), d])
    byMonth.set(m, [...(byMonth.get(m) ?? []), d])
  }
  const years = [...byYear.entries()].map(([year, list]) => {
    const charge = list.reduce((a, d) => a + d.charge, 0)
    const discharge = list.reduce((a, d) => a + d.discharge, 0)
    const load = list.reduce((a, d) => a + d.load, 0)
    const cycles = Math.round(discharge / usable)
    return {
      year, days: list.length, charge: Math.round(charge * 10) / 10, discharge: Math.round(discharge * 10) / 10,
      efficiency: charge > 0 ? Math.round((discharge / charge) * 1000) / 1000 : null,
      cycles, cyclesPerYear: Math.round((cycles * 365) / list.length),
      loadShare: load > 0 ? Math.round((discharge / load) * 1000) / 1000 : null,
      fullDays: list.filter(d => d.charge >= fullThreshold).length,
      maxCharge: Math.max(...list.map(d => d.charge)),
    }
  })
  const months = Array.from({ length: 12 }, (_, idx) => {
    const month = idx + 1
    const list = byMonth.get(month) ?? []
    const charge = list.reduce((a, d) => a + d.charge, 0)
    const discharge = list.reduce((a, d) => a + d.discharge, 0)
    const dischargeByYear: Record<string, number> = {}
    const efficiencyByYear: Record<string, number | null> = {}
    for (const [y, yList] of byYear.entries()) {
      const sub = yList.filter(d => Number(d.day.slice(5, 7)) === month)
      if (sub.length) {
        const sc = sub.reduce((a, d) => a + d.charge, 0)
        const sd = sub.reduce((a, d) => a + d.discharge, 0)
        dischargeByYear[String(y)] = Math.round((sd / sub.length) * 100) / 100
        efficiencyByYear[String(y)] = sc > 0 ? Math.round((sd / sc) * 1000) / 1000 : null
      }
    }
    return {
      month, days: list.length, averageDischarge: list.length ? Math.round((discharge / list.length) * 100) / 100 : 0,
      efficiency: charge > 0 ? Math.round((discharge / charge) * 1000) / 1000 : null,
      fullShare: list.length ? Math.round((list.filter(d => d.charge >= fullThreshold).length / list.length) * 1000) / 1000 : 0,
      dischargeByYear, efficiencyByYear,
    }
  })
  const totalDischarge = days.reduce((a, d) => a + d.discharge, 0)
  return {
    result: {
      years, months, capacity: usable, capacityEstimated: false,
      totalCycles: Math.round(totalDischarge / usable), days: days.length, first: DEMO_START,
      findings: [
        'Der Wirkungsgrad liegt im synthetischen Modell konstant bei rund 88,4 % (0,94 × 0,94).',
        'Im Sommerhalbjahr wird der Speicher regelmäßig voll geladen und deckt den Großteil des Nachtbedarfs.',
      ],
      notes: [
        'Wirkungsgrad = Entladung ÷ Ladung im Zeitraum.',
        'Vollzyklen = Entladung ÷ nutzbare Kapazität (8,6 kWh).',
      ],
    },
  }
}

function computeDataQuality() {
  const { days } = getDataset()
  const byMonth = new Map<string, DaySummary[]>()
  for (const d of days) {
    const m = d.day.slice(0, 7)
    byMonth.set(m, [...(byMonth.get(m) ?? []), d])
  }
  const months = [...byMonth.entries()].map(([month, list]) => {
    const n = list.length
    const intCount = n * 288
    return {
      month, days: n,
      daily: { pv: n, load: n, import: n, export: n, charge: n, discharge: n },
      intervals: { total: intCount, pv: intCount, load: intCount, import: intCount, export: intCount, charge: intCount, discharge: intCount, complete: intCount },
      socSamples: intCount,
    }
  })
  return {
    first: DEMO_START, today: DEMO_END, months, socSamples: days.length * 288,
    sources: [{ point: 'intervals', revision: 'demo-17293-v1', days: days.length, first: DEMO_START, last: DEFAULT_ANCHOR }],
    gaps: { pv: [], load: [], import: [], export: [], charge: [], discharge: [] },
  }
}

function computeSurplus(period: Period, params: URLSearchParams) {
  const { intervals } = getDataset()
  const startMs = localMidnightUtcMs(period.start)
  const endMs = localMidnightUtcMs(period.end)
  const rows = intervals.filter(r => r.startMs >= startMs && r.endMs <= endMs)
  const kw = Number(params.get('kw') || '1')
  const fromMin = Number(params.get('from') || '540')
  const toMin = Number(params.get('to') || '1020')
  const winHours = (r: Interval) => {
    const m = r.localHour * 60 + r.localMinute
    return Math.max(0, Math.min(toMin, m + r.hours * 60) - Math.max(fromMin, m)) / 60
  }
  const captured = (r: Interval) => Math.min(r.export / r.hours, kw) * winHours(r)
  const byDay = new Map<string, { export: number; captured: number; hours: number }>()
  const byMonth = new Map<string, { export: number; captured: number; hours: number }>()
  const bySeason = new Map<string, { export: number; captured: number; hours: number }>()
  const hourTotals = new Array<number>(24).fill(0)
  for (const r of rows) {
    const cap = captured(r)
    const d = byDay.get(r.day) ?? { export: 0, captured: 0, hours: 0 }
    d.export += r.export; d.captured += cap; d.hours += r.hours; byDay.set(r.day, d)
    const mKey = r.day.slice(0, 7)
    const m = byMonth.get(mKey) ?? { export: 0, captured: 0, hours: 0 }
    m.export += r.export; m.captured += cap; m.hours += r.hours; byMonth.set(mKey, m)
    const sKey = selectPeriod(r.day, 'season').start
    const s = bySeason.get(sKey) ?? { export: 0, captured: 0, hours: 0 }
    s.export += r.export; s.captured += cap; s.hours += r.hours; bySeason.set(sKey, s)
    hourTotals[r.localHour] += r.export
  }
  return {
    period, timeZone: ZONE, revision: REVISION,
    export: rows.reduce((a, r) => a + r.export, 0),
    captured: rows.reduce((a, r) => a + captured(r), 0),
    coverage: 1, dailyExport: null,
    days: [...byDay.entries()].map(([day, v]) => ({ day, export: v.export, captured: v.captured })),
    months: [...byMonth.entries()].map(([month, v]) => ({ month, ...v })),
    seasons: [...bySeason.entries()].map(([start, v]) => ({ start, ...v })),
    hoursOfDay: hourTotals.map((exp, hour) => ({ hour, export: rows.length ? exp : null })),
    assumption: 'Flexibel regelbare Last ohne Mindestleistung. Export nach bisheriger Speichernutzung.',
  }
}

function computeHeatmap(period: Period) {
  const { intervals } = getDataset()
  const startMs = localMidnightUtcMs(period.start)
  const endMs = localMidnightUtcMs(period.end)
  const rows = intervals.filter(r => r.startMs >= startMs && r.endMs <= endMs)
  const grouped = new Map<string, { day: string; hour: number; offset: string; import: number; export: number }>()
  for (const r of rows) {
    const key = `${r.day}|${r.localHour}|${r.offsetStr}`
    let g = grouped.get(key)
    if (!g) { g = { day: r.day, hour: r.localHour, offset: r.offsetStr, import: 0, export: 0 }; grouped.set(key, g) }
    g.import += r.import; g.export += r.export
  }
  return { period, timeZone: ZONE, revision: REVISION, points: [...grouped.values()] }
}

const storedSimulations = new Map<string, { result: Potential; model: Map<string, ModelPoint[]>; rawJson: string }>()

function runSimulation(input: {
  period: Period; battery: Battery | null; investment: number | null;
  shift: null | { daily: number; share: number; targetKw: number; source: { from: number; to: number }; target: { from: number; to: number } };
  added: null | { daily: number; kw: number; days: number[]; full: boolean; monthly: number[]; window: { from: number; to: number } };
  curveMax: number | null; exploratory: boolean; emptyStart: boolean;
}) {
  const { intervals, byDay } = getDataset()
  const startMs = localMidnightUtcMs(input.period.start)
  const endMs = localMidnightUtcMs(input.period.end)
  const rows = intervals.filter(r => r.startMs >= startMs && r.endMs <= endMs)
  if (!rows.length) throw new Error('Keine gemeinsamen PV- und Verbrauchsdaten vorhanden.')
  const battery: Battery = input.battery ? { ...input.battery, usable: input.battery.capacity * (input.battery.maxSoc - input.battery.minSoc) } : BASE_BATTERY
  const initial = !input.emptyStart ? BASE_BATTERY.capacity * (rows[0].openingSoc - BASE_BATTERY.minSoc) : 0

  const simulateSeries = (bat: Battery, applyShift: boolean, applyAdded: boolean) => {
    const loads = rows.map(r => r.load)
    const extra = new Array<number>(rows.length).fill(0)
    const moves: { source: string; target: string; kwh: number }[] = []
    let requested = 0

    if (applyShift && input.shift && input.shift.daily > 0) {
      const sh = input.shift
      let idx = 0
      while (idx < rows.length) {
        const day = rows[idx].day
        const dayIndices: number[] = []
        while (idx < rows.length && rows[idx].day === day) { dayIndices.push(idx); idx++ }
        let budget = sh.daily
        for (const s of dayIndices) {
          if (budget <= 1e-6) break
          const rS = rows[s]
          const mS = rS.localHour * 60 + rS.localMinute
          if (mS < sh.source.from || mS >= sh.source.to || rS.import <= 0.01) continue
          for (const t of dayIndices) {
            if (budget <= 1e-6) break
            const rT = rows[t]
            const mT = rT.localHour * 60 + rT.localMinute
            if (mT < sh.target.from || mT >= sh.target.to || rT.export <= 0.01) continue
            const amount = Math.min(0.05, budget, loads[s] * sh.share, rS.import, rT.export, sh.targetKw * rT.hours)
            if (amount > 1e-4) {
              loads[s] -= amount; loads[t] += amount; budget -= amount
              moves.push({ source: rS.start, target: rT.start, kwh: amount })
            }
          }
        }
      }
    }

    if (applyAdded && input.added && input.added.daily > 0) {
      const ad = input.added
      let idx = 0
      while (idx < rows.length) {
        const day = rows[idx].day
        const dow = new Date(day + 'T00:00:00Z').getUTCDay()
        const monthIdx = Number(day.slice(5, 7)) - 1
        const dayIndices: number[] = []
        while (idx < rows.length && rows[idx].day === day) { dayIndices.push(idx); idx++ }
        if (ad.days.includes(dow)) {
          let left = ad.daily * (ad.monthly?.[monthIdx] ?? 1)
          requested += left
          for (const i of dayIndices) {
            const m = rows[i].localHour * 60 + rows[i].localMinute
            if (m < ad.window.from || m >= ad.window.to) continue
            const maxE = ad.kw * rows[i].hours
            const sur = Math.min(left, maxE, rows[i].export)
            extra[i] += sur; left -= sur
          }
          if (ad.full && left > 0) {
            for (const i of dayIndices) {
              const m = rows[i].localHour * 60 + rows[i].localMinute
              if (m < ad.window.from || m >= ad.window.to) continue
              const maxE = ad.kw * rows[i].hours
              const addE = Math.min(left, maxE - extra[i])
              extra[i] += addE; left -= addE
            }
          }
        }
      }
    }

    let stored = Math.min(bat.capacity * (bat.maxSoc - bat.minSoc), initial)
    let openingInv = stored
    let pvSum = 0, loadSum = 0, impSum = 0, expSum = 0, lossSum = 0
    let addedPv = 0, addedBat = 0, addedGrid = 0, addedOpen = 0, costImp = 0, costExp = 0
    const flows = rows.map((r, i) => {
      const f = dispatchStep(bat, stored, r.hours, r.pv, loads[i], extra[i], openingInv)
      stored = f.stored; openingInv = f.openingInventory
      pvSum += r.pv; loadSum += loads[i] + extra[i]; impSum += f.import; expSum += f.export
      lossSum += f.chargeLoss + f.dischargeLoss
      addedPv += f.addedPv; addedBat += f.addedBattery; addedGrid += f.addedGrid; addedOpen += f.addedOpeningBattery
      costImp += f.import * (tariffPrice(0, r.day) ?? 0.30)
      costExp += f.export * (tariffPrice(1, r.day) ?? 0.08)
      return f
    })
    let fixed = 0
    for (let d = input.period.start; d < input.period.end; d = addDays(d, 1))
      fixed += (tariffPrice(2, d) ?? 12) / daysInMonth(d)
    const net = costImp + fixed - costExp
    const summary: Comparison = {
      pv: pvSum, load: loadSum, import: impSum, export: expSum, loss: lossSum, end: stored,
      autarky: loadSum > 0 ? (loadSum - impSum) / loadSum : null,
      cost: { import: costImp, export: costExp, fixed, importCoverage: 1, exportCoverage: 1, complete: true, coveredNet: net, net },
    }
    return { summary, flows, loads, extra, moves, requested, addedPv, addedBat, addedGrid, addedOpen }
  }

  const refRun = simulateSeries(BASE_BATTERY, false, false)
  const scRun = simulateSeries(battery, !!input.shift, !!input.added)
  const rep = createReport(input.period)
  const savings = (refRun.summary.cost.net ?? 0) - (scRun.summary.cost.net ?? 0)

  let additional: Record<string, number | null> | null = null
  if (input.added) {
    const houseRun = simulateSeries(battery, !!input.shift, false)
    const served = scRun.extra.reduce((a, v) => a + v, 0)
    const gridCost = rows.reduce((a, r, i) => a + scRun.extra[i] * (tariffPrice(0, r.day) ?? 0.30), 0)
    const totalCostChange = (scRun.summary.cost.net ?? 0) - (houseRun.summary.cost.net ?? 0)
    additional = {
      requested: scRun.requested, served, pv: scRun.addedPv, battery: scRun.addedBat,
      openingBattery: scRun.addedOpen, grid: scRun.addedGrid, unserved: scRun.requested - served,
      extraGrid: scRun.summary.import - houseRun.summary.import, remainingExport: scRun.summary.export,
      totalCostChange, savingsAgainstGrid: gridCost - totalCostChange,
    }
  }

  const curve: { capacity: number; avoidedImport: number; savings: number | null; marginalImport: number | null }[] = []
  if (input.curveMax) {
    const max = Math.max(input.curveMax, battery.capacity, BASE_BATTERY.capacity)
    const caps = [...new Set([...Array.from({ length: 7 }, (_, i) => Math.round((max * i) / 6 * 10) / 10), BASE_BATTERY.capacity, battery.capacity])].sort((a, b) => a - b)
    for (const c of caps) {
      const r = simulateSeries({ ...battery, capacity: c }, false, false)
      const avoided = refRun.summary.import - r.summary.import
      const prev = curve.at(-1)
      curve.push({
        capacity: c, avoidedImport: avoided, savings: (refRun.summary.cost.net ?? 0) - (r.summary.cost.net ?? 0),
        marginalImport: prev ? (avoided - prev.avoidedImport) / (c - prev.capacity) : null,
      })
    }
  }

  const daily: { day: string; measuredImport: number | null; referenceImport: number; scenarioImport: number; measuredExport: number | null; referenceExport: number; scenarioExport: number }[] = []
  const modelMap = new Map<string, ModelPoint[]>()
  for (let d = input.period.start; d < input.period.end; d = addDays(d, 1)) {
    const dayRows = byDay.get(d)
    if (!dayRows?.length) continue
    let mImp = 0, rImp = 0, sImp = 0, mExp = 0, rExp = 0, sExp = 0
    const mPoints: ModelPoint[] = []
    for (const r of dayRows) {
      const idx = rows.indexOf(r)
      if (idx < 0) continue
      const rf = refRun.flows[idx], sf = scRun.flows[idx]
      mImp += r.import; rImp += rf.import; sImp += sf.import
      mExp += r.export; rExp += rf.export; sExp += sf.export
      mPoints.push({
        start: r.start, importKw: sf.import / r.hours, exportKw: sf.export / r.hours,
        soc: battery.minSoc + (battery.capacity > 0 ? sf.stored / battery.capacity : 0),
        loadKw: (scRun.loads[idx] + scRun.extra[idx]) / r.hours,
      })
    }
    daily.push({ day: d, measuredImport: mImp, referenceImport: rImp, scenarioImport: sImp, measuredExport: mExp, referenceExport: rExp, scenarioExport: sExp })
    modelMap.set(d, mPoints)
  }

  const id = crypto.randomUUID()
  const result: Potential = {
    reference: refRun.summary, scenario: scRun.summary, measured: rep.metrics,
    validation: { importError: 0, exportError: 0, normalizedError: 0, socMaePoints: 0, sufficient: true, socValidated: true },
    savings, paybackYears: null, paybackStatus: 'Nur bei validierter Speichererweiterung und zwölf vollständigen Monaten.',
    coverage: rep.metrics.coverage, resets: 0, initialReference: initial, initialScenario: Math.min(battery.usable ?? 8.64, initial),
    warnings: [
      'Historischer Rückblick. AC-Intervallmodell ohne Netzladen und Batterieexport.',
      'Der Endbestand ist in der Ersparnis nicht gutgeschrieben.',
    ],
    curve, daily, additional, moves: scRun.moves,
  }
  const rawJson = JSON.stringify({
    parameters: {
      period: input.period, battery: input.battery ?? BASE_BATTERY,
      shift: input.shift, added: input.added, investment: input.investment,
      curveMax: input.curveMax, exploratory: input.exploratory, emptyStart: input.emptyStart,
    },
    revision: REVISION, engine: '1.0-static-demo', timeZone: ZONE, ...result,
  }, null, 2)
  storedSimulations.set(id, { result, model: modelMap, rawJson })
  return id
}

export function buildCsvExport(queryStr: string): string {
  const params = new URLSearchParams(queryStr)
  const period = resolvePeriod(params)
  const rep = createReport(period)
  const buckets = buildBuckets(period)
  const singleDay = Math.round((Date.parse(period.end) - Date.parse(period.start)) / 86_400_000) === 1
  const unit = singleDay ? 'kW' : 'kWh'
  const lines = [
    'Beginn;Ende_exklusiv;Zeitzone;Revision;PV_kWh;Verbrauch_kWh;Bezug_kWh;Export_kWh;Abdeckung;Netto_EUR',
    `${period.start};${period.end};${ZONE};${REVISION};${rep.metrics.pv.kwh ?? ''};${rep.metrics.load.kwh ?? ''};${rep.metrics.import.kwh ?? ''};${rep.metrics.export.kwh ?? ''};${rep.metrics.coverage};${rep.cost.net ?? ''}`,
    '',
    `Zeit;PV_${unit};Verbrauch_${unit};Bezug_${unit};Export_${unit};Ladung_${unit};Entladung_${unit};SOC;Netto_EUR`,
  ]
  for (const b of buckets) {
    lines.push([singleDay ? b.start : b.day, b.pv, b.load, b.import, b.export, b.charge, b.discharge, b.soc, b.cost].map(v => v ?? '').join(';'))
  }
  return lines.join('\r\n')
}

export function getSimulationExport(id: string): string {
  return storedSimulations.get(id)?.rawJson ?? '{}'
}

export async function handleStaticDemoRequest<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  if (method !== 'GET') {
    if (path === '/live/activity' && method === 'POST') return undefined as T
    if (path === '/live/probe' && method === 'POST') {
      const { byDay } = getDataset()
      const last = byDay.get(DEFAULT_ANCHOR)?.[144]
      return {
        measuredAt: last?.start ?? `${DEFAULT_ANCHOR}T12:00:00.000Z`,
        values: {
          pv: (last?.pv ?? 0) * 12, load: (last?.load ?? 0) * 12,
          grid: ((last?.import ?? 0) - (last?.export ?? 0)) * 12,
          battery: ((last?.charge ?? 0) - (last?.discharge ?? 0)) * 12,
          soc: last?.soc ?? 0.65,
        },
        missing: [],
      } as T
    }
    if (path === '/simulations' && method === 'POST') {
      const id = runSimulation(body as Parameters<typeof runSimulation>[0])
      return { id } as T
    }
    if (path.startsWith('/simulations/') && method === 'DELETE') {
      storedSimulations.delete(path.slice('/simulations/'.length))
      return undefined as T
    }
    throw new Error('Diese Änderung ist im Demomodus gesperrt.')
  }

  const [route, qs = ''] = path.split('?')
  const params = new URLSearchParams(qs)

  if (route === '/auth/session') return { demo: true, authenticated: false, csrf: 'static-demo' } as T
  if (route === '/plant') {
    const plant: Plant = {
      name: 'Synthetische Referenzanlage', zone: ZONE, region: 'DE', synthetic: true,
      firstDay: DEMO_START, defaultAnchor: DEFAULT_ANCHOR, providerStatus: 'Synthetische Demodaten',
      capabilities: { energy: true, timeseries: true, events: true, simulation: true },
      devices: [{ model: 'Demo-Hybridwechselrichter', type: 'Synthetisch' }],
    }
    return plant as T
  }
  if (route === '/settings/plant') {
    const settings: Settings = {
      name: 'Synthetische Referenzanlage', zone: ZONE, region: 'DE', rule: DEFAULT_RULE,
      configurations: [{ since: DEMO_START, pvKwp: 10, origin: 'synthetic', battery: BASE_BATTERY }],
    }
    return settings as T
  }
  if (route === '/tariffs') return TARIFFS as T
  if (route === '/live/mapping') {
    return {
      mapping: {
        deviceKey: 'demo_14_0_0', pv: 1, load: 13119, grid: 13149, battery: 13126, soc: 13141,
        positiveGridIsImport: true, positiveBatteryIsCharge: true, socPercent: true,
        confirmed: true, batteryExportExcluded: true, gridExport: 13121, batteryDischarge: 13150,
      },
      devices: [{ key: 'demo_14_0_0', model: 'Demo-Hybridwechselrichter', type: 'Synthetisch', candidateProbeSupported: false }],
    } as T
  }
  if (route === '/live/now') {
    const { byDay } = getDataset()
    const rows = byDay.get(DEFAULT_ANCHOR) ?? []
    const now = new Date()
    const slot = Math.min(rows.length - 1, Math.floor(((now.getHours() * 60 + now.getMinutes()) / 5)))
    const r = rows[Math.max(0, slot)]
    const at = new Date(Math.floor(now.getTime() / 300_000) * 300_000).toISOString()
    const pvKw = r.pv / r.hours, loadKw = r.load / r.hours
    const gridKw = (r.import - r.export) / r.hours
    const batKw = (r.charge - r.discharge) / r.hours
    const mk = (val: number, dir: string) => ({ value: val, direction: dir, measuredAt: at, ageSeconds: 25, quality: 'confirmed' })
    return {
      pv: mk(pvKw, pvKw > 0.01 ? 'production' : 'idle'),
      load: mk(loadKw, 'consumption'),
      grid: mk(gridKw, gridKw > 0.01 ? 'import' : gridKw < -0.01 ? 'export' : 'idle'),
      battery: mk(batKw, batKw > 0.01 ? 'charge' : batKw < -0.01 ? 'discharge' : 'idle'),
      soc: mk(r.soc, 'state'),
      pvSurplusKw: gridKw < -0.01 ? -gridKw : 0,
      flowConfirmed: true, quality: 'synthetic', fetchedAt: at,
    } as T
  }
  if (route === '/live/today') {
    const { byDay } = getDataset()
    const rows = byDay.get(DEFAULT_ANCHOR) ?? []
    const samples = rows.flatMap(r => [
      { channel: 'pv', at: r.start, end: null, value: r.pv / r.hours, source: 'synthetic' },
      { channel: 'load', at: r.start, end: null, value: r.load / r.hours, source: 'synthetic' },
      { channel: 'grid', at: r.start, end: null, value: (r.import - r.export) / r.hours, source: 'synthetic' },
      { channel: 'battery', at: r.start, end: null, value: (r.charge - r.discharge) / r.hours, source: 'synthetic' },
      { channel: 'soc', at: r.start, end: null, value: r.soc, source: 'synthetic' },
    ])
    return { day: DEFAULT_ANCHOR, start: rows[0]?.start, end: rows.at(-1)?.end, gaps: [], samples } as T
  }

  const period = resolvePeriod(params)
  if (route === '/overview') {
    const report = createReport(period)
    const compare = params.get('compare')
    const kind = params.get('kind') || 'month'
    const comparison = compare === 'previous' || compare === 'year'
      ? createReport(comparePeriod(period, kind, compare === 'year')) : null
    return { report, comparison, buckets: buildBuckets(period), providerBuckets: null } as T
  }
  if (route === '/timeseries') {
    const spanDays = Math.round((Date.parse(period.end) - Date.parse(period.start)) / 86_400_000)
    return { report: createReport(period), points: buildBuckets(period), unit: spanDays === 1 ? 'kW' : 'kWh' } as T
  }
  if (route === '/heatmap') return computeHeatmap(period) as T
  if (route === '/events') {
    const offset = Number(params.get('offset') || '0')
    const limit = Number(params.get('limit') || '30')
    const threshold = Number(params.get('threshold') || '0.1')
    const type = params.get('type') || ''
    const { events, observations, intervals } = computeEvents(period, threshold, type)
    return {
      period, timeZone: ZONE, revision: REVISION, total: events.length,
      batteryEstimated: false, intervals, observations,
      events: events.slice(offset, offset + limit),
    } as T
  }
  if (route === '/surplus') return computeSurplus(period, params) as T
  if (route === '/energy-insights') {
    const hour = params.has('hour') ? Number(params.get('hour')) : null
    const mix = params.has('mix') ? Number(params.get('mix')) : null
    const offset = Number(params.get('offset') || '0')
    return computeEnergyInsights(period, hour, mix, offset) as T
  }
  if (route === '/storage-potential') return computeStoragePotential(period, params) as T
  if (route === '/daily') {
    const { days } = getDataset()
    const inDays = days.filter(d => d.day >= period.start && d.day < period.end)
    return {
      period,
      days: inDays.map(d => ({
        day: d.day, pv: d.pv, load: d.load, import: d.import, export: d.export,
        charge: d.charge, discharge: d.discharge,
        autarky: d.load > 0 ? (d.load - d.import) / d.load : null, flag: null,
      })),
    } as T
  }
  if (route === '/economics') return computeEconomics(period) as T
  if (route === '/battery-health') return computeBatteryHealth() as T
  if (route === '/data-quality') return computeDataQuality() as T
  if (route === '/data-status') {
    const report = createReport(period)
    const { intervals } = getDataset()
    const startMs = localMidnightUtcMs(period.start)
    const endMs = localMidnightUtcMs(period.end)
    const count = intervals.filter(r => r.startMs >= startMs && r.endMs <= endMs).length
    return {
      report, latest: `${DEMO_END}T00:00:00.000Z`, lastSuccess: `${DEMO_END}T00:00:00.000Z`,
      provider: 'Synthetische Demodaten', progress: null,
      job: {
        id: 'demo-import', windows: 53, emptyWindows: 0, readingRows: 210240, aggregateRows: 0,
        cancelled: null, status: 'completed', checkpoint: `${DEMO_END}T00:00:00.000Z`,
        start: `${DEMO_START}T00:00:00.000Z`, end: `${DEMO_END}T00:00:00.000Z`,
        error: null, lastAttempt: `${DEMO_END}T00:00:00.000Z`, lastSuccess: `${DEMO_END}T00:00:00.000Z`,
      },
      repair: [], intervalCounts: { total: count, pv: count, load: count, both: count }, gaps: [],
    } as T
  }
  if (route.startsWith('/simulations/')) {
    const id = route.slice('/simulations/'.length)
    const sim = storedSimulations.get(id)
    if (!sim) throw new Error('Simulation nicht gefunden.')
    const day = params.get('day')
    return {
      state: 'completed', progress: 100, error: null,
      result: sim.result, model: day ? (sim.model.get(day) ?? null) : null,
    } as T
  }
  throw new Error('Unbekannter Demo-Endpunkt: ' + path)
}
