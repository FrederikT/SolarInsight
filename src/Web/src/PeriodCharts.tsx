import { useMemo, useState } from 'react'
import type { EChartsCoreOption } from 'echarts/core'
import type { Point } from './api'
import { Chart, date, number, pct } from './components'

/** Fixed colours per meaning, shared by all period charts. */
export const flowColors = { pv: '#b67715', direct: '#e3a33a', battery: '#6d4dac', grid: '#a8324e', unsplit: '#8d9a92',
  export: '#008269', load: '#165eaa', charge: '#8f78c4', soc: '#397577' }

type Size = 'day' | 'week' | 'month'
type View = 'balance' | 'channels' | 'ratios' | 'storage'
type Bucket = { label: string; start: string; end: string; days: number; pv: number | null; load: number | null;
  import: number | null; export: number | null; charge: number | null; discharge: number | null;
  direct: number | null; unsplit: number | null; soc: number | null; socMin: number | null; socMax: number | null }

const monthNames = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez']
const parse = (day: string) => new Date(day.slice(0, 10) + 'T00:00:00Z')
const iso = (d: Date) => d.toISOString().slice(0, 10)
const addDays = (day: string, count: number) => iso(new Date(+parse(day) + count * 86_400_000))
function isoWeek(day: string) {
  const d = parse(day)
  const thursday = new Date(+d + (3 - (d.getUTCDay() + 6) % 7) * 86_400_000)
  const jan4 = new Date(Date.UTC(thursday.getUTCFullYear(), 0, 4))
  const week = 1 + Math.round((+thursday - +jan4) / 604_800_000 - (3 - (jan4.getUTCDay() + 6) % 7) / 7)
  return { week, year: thursday.getUTCFullYear(), monday: addDays(day, -((d.getUTCDay() + 6) % 7)) }
}
const sum = (values: (number | null)[]) => values.some(v => v != null) ? values.reduce<number>((a, v) => a + (v ?? 0), 0) : null
const mean = (values: (number | null)[]) => {
  const valid = values.filter((v): v is number => v != null)
  return valid.length ? valid.reduce((a, v) => a + v, 0) / valid.length : null
}

/**
 * Splits each daily point into household sources before any aggregation: with battery values the direct PV
 * share is load − import − discharge; without them only load − import is known (PV direct or battery).
 * Deriving per day first keeps weekly/monthly sums consistent when battery data starts mid-bucket.
 */
function derive(p: Point) {
  const known = p.load != null && p.import != null
  return { direct: known && p.discharge != null ? Math.max(0, p.load! - p.import! - p.discharge) : null,
    unsplit: known && p.discharge == null ? Math.max(0, p.load! - p.import!) : null }
}

/** Groups backend buckets (daily, or monthly for long periods) into the chosen chart resolution. */
export function group(points: Point[], size: Size, periodEnd: string): Bucket[] {
  const monthlyInput = points.length > 1 && Date.parse(points[1].day!) - Date.parse(points[0].day!) > 27 * 86_400_000
  const keyed = new Map<string, { label: string; start: string; items: Point[] }>()
  for (const p of points) {
    if (!p.day) continue
    const day = p.day
    let key = day, label = date(day)
    if (size === 'week' && !monthlyInput) { const w = isoWeek(day); key = w.monday; label = 'KW ' + w.week + '/' + String(w.year).slice(2) }
    if (size === 'month' || monthlyInput) { key = day.slice(0, 7) + '-01'; label = monthNames[parse(day).getUTCMonth()] + ' ' + day.slice(0, 4) }
    if (!keyed.has(key)) keyed.set(key, { label, start: day, items: [] })
    keyed.get(key)!.items.push(p)
  }
  const groups = [...keyed.values()]
  return groups.map((g, i) => {
    const derived = g.items.map(derive)
    const end = groups[i + 1]?.start ?? periodEnd
    return { label: g.label, start: g.start, end, days: Math.round((Date.parse(end) - Date.parse(g.start)) / 86_400_000),
      pv: sum(g.items.map(p => p.pv)), load: sum(g.items.map(p => p.load)), import: sum(g.items.map(p => p.import)),
      export: sum(g.items.map(p => p.export)), charge: sum(g.items.map(p => p.charge)), discharge: sum(g.items.map(p => p.discharge)),
      direct: sum(derived.map(d => d.direct)), unsplit: sum(derived.map(d => d.unsplit)),
      soc: mean(g.items.map(p => p.soc)), socMin: g.items.some(p => p.socMin != null) ? Math.min(...g.items.flatMap(p => p.socMin ?? [])) : null,
      socMax: g.items.some(p => p.socMax != null) ? Math.max(...g.items.flatMap(p => p.socMax ?? [])) : null }
  })
}

const ratio = (total: number | null, part: number | null) => total != null && part != null && total > 0 ? 100 * (1 - part / total) : null

function options(labels: string[], series: object[], yAxis: object | object[], legend = true) {
  return { animation: false, legend: legend ? { type: 'scroll' } : undefined,
    tooltip: { trigger: 'axis', renderMode: 'richText', valueFormatter: (v: unknown) => typeof v === 'number' ? number(v, 1) : '—' },
    grid: { left: 62, right: Array.isArray(yAxis) ? 58 : 24, top: 52, bottom: labels.length > 20 ? 70 : 45 },
    xAxis: { type: 'category', data: labels }, yAxis, series,
    dataZoom: labels.length > 40 ? [{ type: 'inside' }, { type: 'slider', height: 18 }] : [] }
}

export function PeriodHistory({ points, periodEnd, drill }: {
  points: Point[]; periodEnd: string; drill: (day: string, exclusive?: string | null) => void
}) {
  const spanDays = points.length ? Math.round((Date.parse(periodEnd) - Date.parse(points[0].day!)) / 86_400_000) : 0
  const automatic: Size = spanDays <= 35 ? 'day' : spanDays <= 120 ? 'week' : 'month'
  const [chosen, setSize] = useState<Size | ''>('')
  const [view, setView] = useState<View>('balance')
  const monthlyInput = points.length > 1 && Date.parse(points[1].day!) - Date.parse(points[0].day!) > 27 * 86_400_000
  const size: Size = monthlyInput ? 'month' : chosen || automatic
  const buckets = useMemo(() => group(points, size, periodEnd), [points, size, periodEnd])
  const labels = buckets.map(b => b.label)
  const click = (label: string) => {
    const b = buckets.find(x => x.label === label)
    if (b) drill(b.start, b.days > 1 ? b.end : null)
  }
  const kwh = { type: 'value', name: 'kWh', axisLabel: { formatter: (v: number) => number(v, 0) } }
  const percent = { type: 'value', name: '%', min: 0, max: 100 }
  const bar = (name: string, color: string, data: (number | null)[], stack?: string) =>
    ({ name, type: 'bar', stack, data, itemStyle: { color }, emphasis: { focus: 'series' } })
  const line = (name: string, color: string, data: (number | null)[], extra: object = {}) =>
    ({ name, type: 'line', data, showSymbol: buckets.length < 40, connectNulls: false, itemStyle: { color }, lineStyle: { color, width: 2 }, ...extra })
  const hasBattery = buckets.some(b => b.discharge != null)
  const hasUnsplit = buckets.some(b => b.unsplit != null && b.unsplit > 0)
  let option: EChartsCoreOption
  let note: string
  if (view === 'balance') {
    // Above zero: where the household energy came from. Below zero: PV energy exported to the grid.
    option = options(labels, [
      bar('PV direkt genutzt', flowColors.direct, buckets.map(b => b.direct), 'house'),
      bar('Aus dem Speicher', flowColors.battery, buckets.map(b => b.discharge), 'house'),
      ...(hasUnsplit ? [bar('PV direkt oder Speicher (nicht getrennt)', flowColors.unsplit, buckets.map(b => b.unsplit), 'house')] : []),
      bar('Netzbezug', flowColors.grid, buckets.map(b => b.import), 'house'),
      bar('Einspeisung (negativ)', flowColors.export, buckets.map(b => b.export == null ? null : -b.export), 'export'),
      line('PV-Erzeugung', flowColors.pv, buckets.map(b => b.pv), { type: 'line', lineStyle: { color: flowColors.pv, width: 2, type: 'dashed' } }),
    ], { ...kwh, axisLabel: { formatter: (v: number) => number(v, 0) } })
    note = 'Balken oberhalb der Nulllinie zeigen, woher der Hausverbrauch kam (Summe = Hausverbrauch). Unterhalb steht die Einspeisung. Die gestrichelte Linie ist die PV-Erzeugung.' +
      (hasUnsplit ? ' Grau: Tage ohne Batteriewerte, an denen PV-Direktverbrauch und Speicher nicht getrennt werden können.' : '')
  } else if (view === 'channels') {
    option = options(labels, [
      line('PV-Erzeugung', flowColors.pv, buckets.map(b => b.pv)), line('Hausverbrauch', flowColors.load, buckets.map(b => b.load)),
      line('Netzbezug', flowColors.grid, buckets.map(b => b.import)), line('Einspeisung', flowColors.export, buckets.map(b => b.export)),
      ...(hasBattery ? [line('Batterieladung', flowColors.charge, buckets.map(b => b.charge)), line('Batterieentladung', flowColors.battery, buckets.map(b => b.discharge))] : []),
    ], kwh)
    note = 'Einzelne Reihen lassen sich über die Legende aus- und einblenden.'
  } else if (view === 'ratios') {
    option = options(labels, [
      line('Autarkiegrad', flowColors.grid, buckets.map(b => ratio(b.load, b.import))),
      line('Eigenverbrauchsquote', flowColors.export, buckets.map(b => ratio(b.pv, b.export))),
    ], percent)
    note = 'Autarkiegrad = (Hausverbrauch − Netzbezug) ÷ Hausverbrauch. Eigenverbrauchsquote = (PV − Einspeisung) ÷ PV. Je Balken aus den erfassten Tagen berechnet.'
  } else {
    const hasSoc = buckets.some(b => b.soc != null)
    option = options(labels, [
      bar('Batterieladung', flowColors.charge, buckets.map(b => b.charge)),
      bar('Batterieentladung', flowColors.battery, buckets.map(b => b.discharge)),
      ...(hasSoc ? [line('Mittlerer Ladezustand', flowColors.soc, buckets.map(b => b.soc == null ? null : 100 * b.soc), { yAxisIndex: 1 }),
        line('Minimaler Ladezustand', flowColors.soc, buckets.map(b => b.socMin == null ? null : 100 * b.socMin), { yAxisIndex: 1, lineStyle: { color: flowColors.soc, type: 'dotted' } }),
        line('Maximaler Ladezustand', flowColors.soc, buckets.map(b => b.socMax == null ? null : 100 * b.socMax), { yAxisIndex: 1, lineStyle: { color: flowColors.soc, type: 'dashed' } })] : []),
    ], [kwh, percent])
    note = hasSoc ? 'Ladung und Entladung in kWh (linke Achse), Ladezustand in % (rechte Achse). Der Ladezustand stammt aus historischen Gerätestichproben.'
      : 'Für diesen Zeitraum liegen keine Ladezustandswerte vor. Historische Stichproben sind ab dem ersten Gerätezählerimport verfügbar.'
    if (!hasBattery) note = 'Für diesen Zeitraum liegen keine Batteriewerte vor. ' + note
  }
  const empty = !buckets.some(b => b.pv != null || b.load != null || b.import != null || b.export != null)
  return <>
    <div className="row">
      <label>Darstellung<select value={view} onChange={e => setView(e.target.value as View)}>
        <option value="balance">Energiebilanz (gestapelt)</option><option value="channels">Einzelne Messgrößen (Linien)</option>
        <option value="ratios">Autarkie und Eigenverbrauch</option><option value="storage">Speicher und Ladezustand</option></select></label>
      <label>Auflösung<select value={size} disabled={monthlyInput} onChange={e => setSize(e.target.value as Size)}>
        <option value="day">Tage</option><option value="week">Wochen</option><option value="month">Monate</option></select></label>
    </div>
    {empty ? <p className="notice">Für diesen Zeitraum liegen keine Energiewerte vor.</p> :
      <Chart label="Verlauf im Zeitraum" option={option} click={click} />}
    <p><small>{note} Ein Klick auf einen Balken öffnet den Zeitraum.</small></p>
    <details><summary>Diagrammdaten als Tabelle</summary><div className="table"><table><thead><tr><th>Zeitraum</th>
      <th>PV (kWh)</th><th>Verbrauch (kWh)</th><th>Netzbezug (kWh)</th><th>Einspeisung (kWh)</th><th>Ladung (kWh)</th>
      <th>Entladung (kWh)</th><th>Autarkie</th><th>Ø Ladezustand</th></tr></thead><tbody>
      {buckets.map(b => <tr key={b.start}><td><button className="link" onClick={() => click(b.label)}>{b.label}</button></td>
        <td>{number(b.pv, 1)}</td><td>{number(b.load, 1)}</td><td>{number(b.import, 1)}</td><td>{number(b.export, 1)}</td>
        <td>{number(b.charge, 1)}</td><td>{number(b.discharge, 1)}</td><td>{pct(ratio(b.load, b.import) == null ? null : ratio(b.load, b.import)! / 100)}</td>
        <td>{pct(b.soc)}</td></tr>)}</tbody></table></div></details>
  </>
}

const seasons = [['Frühling', [2, 3, 4]], ['Sommer', [5, 6, 7]], ['Herbst', [8, 9, 10]], ['Winter', [11, 0, 1]]] as const

/** Month-of-year comparison across years and seasonal daily averages, for periods spanning a year or more. */
export function LongTermCharts({ points, periodEnd }: { points: Point[]; periodEnd: string }) {
  const [metric, setMetric] = useState<'pv' | 'load' | 'import' | 'autarky'>('pv')
  const months = useMemo(() => group(points, 'month', periodEnd), [points, periodEnd])
  const years = [...new Set(months.map(m => m.start.slice(0, 4)))]
  if (months.length < 10) return null
  const value = (m: Bucket) => metric === 'autarky' ? ratio(m.load, m.import) : m[metric]
  // The latest year as solid bars next to the lighter year before, older years as dashed lines.
  const latest = years[years.length - 1], before = years[years.length - 2]
  const valuesOf = (year: string) => monthNames.map((_, month) => {
    const m = months.find(x => x.start.startsWith(year + '-' + String(month + 1).padStart(2, '0'))); return m ? value(m) : null })
  const tone = metric === 'pv' ? flowColors.pv : metric === 'load' ? flowColors.load : metric === 'import' ? flowColors.grid : flowColors.export
  const yearSeries = [
    ...(before ? [{ name: before, type: 'bar', barGap: '8%', data: valuesOf(before), itemStyle: { color: tone, opacity: .35 } }] : []),
    { name: latest, type: 'bar', data: valuesOf(latest), itemStyle: { color: tone } },
    ...years.slice(0, -2).map(year => ({ name: year, type: 'line', data: valuesOf(year), showSymbol: false,
      lineStyle: { width: 1.5, type: 'dashed' }, itemStyle: { color: '#8d9a92' } })),
  ]
  const perDay = (season: readonly number[], key: 'pv' | 'load' | 'import' | 'export') => {
    const selected = months.filter(m => season.includes(parse(m.start).getUTCMonth()) && m[key] != null)
    const days = selected.reduce((a, m) => a + m.days, 0)
    return days ? selected.reduce((a, m) => a + m[key]!, 0) / days : null
  }
  const labels = { pv: 'PV-Erzeugung (kWh)', load: 'Hausverbrauch (kWh)', import: 'Netzbezug (kWh)', autarky: 'Autarkiegrad (%)' }
  return <>
    <h3>Monate im Jahresvergleich</h3>
    <label>Kennzahl<select value={metric} onChange={e => setMetric(e.target.value as typeof metric)}>
      {Object.entries(labels).map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
    <Chart label="Monatswerte je Jahr" option={options(monthNames, yearSeries,
      metric === 'autarky' ? { type: 'value', name: '%', min: 0, max: 100 } : { type: 'value', name: 'kWh' })} />
    <p><small>Kräftige Balken: {latest}{before ? ', helle Balken daneben: ' + before : ''}{years.length > 2 ? ', gestrichelte Linien: frühere Jahre' : ''}. Laufende oder unvollständige Monate liegen niedriger, ohne dass weniger erzeugt wurde.</small></p>
    <h3>Jahreszeiten im Mittel pro Tag</h3>
    <Chart label="Tagesmittel je Jahreszeit" option={options(seasons.map(s => s[0]), [
      { name: 'PV-Erzeugung', type: 'bar', itemStyle: { color: flowColors.pv }, data: seasons.map(s => perDay(s[1], 'pv')) },
      { name: 'Hausverbrauch', type: 'bar', itemStyle: { color: flowColors.load }, data: seasons.map(s => perDay(s[1], 'load')) },
      { name: 'Netzbezug', type: 'bar', itemStyle: { color: flowColors.grid }, data: seasons.map(s => perDay(s[1], 'import')) },
      { name: 'Einspeisung', type: 'bar', itemStyle: { color: flowColors.export }, data: seasons.map(s => perDay(s[1], 'export')) },
    ], { type: 'value', name: 'kWh/Tag' })} />
    <p><small>Meteorologische Jahreszeiten (Frühling März–Mai usw.) über alle erfassten Jahre, als kWh je Kalendertag.</small></p>
  </>
}
