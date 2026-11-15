import { useState } from 'react'
import { useData } from './api'
import { Chart, date, number, Panel, pct, Status, Tile, tones } from './components'

export type Ring = { total: number; direct: number; battery: number; grid: number; unsplit?: number }
export type Balance = { household: Ring | null; production: Ring | null; gridCharge: number; batteryExport: number }

/**
 * Energy flow as a Sankey diagram: sources on the left (PV, grid import), the battery in the middle, sinks on the
 * right (household, export). Energy that is PV-direct or battery but not separable gets its own middle node,
 * so no split is invented for days without battery values.
 */
export function EnergyFlow({ balance }: { balance: Balance }) {
  const h = balance.household, p = balance.production
  if (!h || !p || !h.total) return null
  const nodes = [
    { name: 'PV-Erzeugung', color: tones.pv }, { name: 'Netzbezug', color: tones.grid },
    { name: 'Speicher', color: tones.battery }, { name: 'Nicht trennbar', color: '#8d9a92' },
    { name: 'Hausverbrauch', color: tones.house }, { name: 'Einspeisung', color: tones.export }]
  const links = [
    ['PV-Erzeugung', 'Hausverbrauch', h.direct], ['PV-Erzeugung', 'Speicher', p.battery],
    ['PV-Erzeugung', 'Einspeisung', p.grid], ['PV-Erzeugung', 'Nicht trennbar', p.unsplit ?? 0],
    ['Nicht trennbar', 'Hausverbrauch', h.unsplit ?? 0], ['Speicher', 'Hausverbrauch', h.battery],
    ['Netzbezug', 'Hausverbrauch', h.grid], ['Netzbezug', 'Speicher', balance.gridCharge],
    ['Speicher', 'Einspeisung', balance.batteryExport],
  ].filter(l => (l[2] as number) > .05).map(([source, target, value]) => ({ source, target, value }))
  const used = new Set(links.flatMap(l => [l.source, l.target]))
  return <Chart className="tall" label="Energiefluss im Zeitraum" option={{
    animation: false,
    tooltip: { trigger: 'item', renderMode: 'richText', formatter: (x: { dataType: string; data: { source?: string; target?: string; value?: number }; name: string; value: number }) =>
      x.dataType === 'edge' ? `${x.data.source} → ${x.data.target}: ${number(x.data.value, 1)} kWh` : `${x.name}: ${number(x.value, 1)} kWh` },
    series: [{ type: 'sankey', left: 8, right: 120, top: 16, bottom: 16, nodeGap: 18, nodeWidth: 16, draggable: false,
      emphasis: { focus: 'adjacency' }, layoutIterations: 0,
      data: nodes.filter(n => used.has(n.name)).map(n => ({ name: n.name, itemStyle: { color: n.color, borderWidth: 0 } })),
      links, lineStyle: { color: 'gradient', opacity: .38, curveness: .5 },
      label: { fontSize: 13, fontWeight: 600, lineHeight: 18, formatter: (x: { name: string; value: number }) => `${x.name}\n${number(x.value, 0)} kWh` } }],
  }} />
}

type Day = { day: string; pv: number | null; load: number | null; import: number | null; export: number | null;
  charge: number | null; discharge: number | null; autarky: number | null; flag: string | null }
const metrics = {
  autarky: { label: 'Autarkie', unit: '%', pick: (d: Day) => d.autarky == null ? null : d.autarky * 100, colors: ['#f3d9d4', '#e7b45f', '#2f8f62'] },
  pv: { label: 'PV-Ertrag', unit: 'kWh', pick: (d: Day) => d.pv, colors: ['#fbf1dc', '#e3a33a', '#8a4f05'] },
  import: { label: 'Netzbezug', unit: 'kWh', pick: (d: Day) => d.import, colors: ['#f6e7ea', '#c7607a', '#6e1730'] },
  discharge: { label: 'Aus dem Speicher', unit: 'kWh', pick: (d: Day) => d.discharge, colors: ['#ece7f6', '#8f78c4', '#3d2775'] },
} as const

/**
 * One square per day, coloured by the chosen figure. Empty squares are days without values; outage and catch-up
 * days are marked, because their energy belongs to several days. A click opens the day.
 */
export function YearCalendar({ query, drill }: { query: string; drill: (day: string) => void }) {
  const [metric, setMetric] = useState<keyof typeof metrics>('autarky')
  const state = useData<{ days: Day[] }>('/daily?' + query)
  if (!state?.data) return <Status error={state?.error} text="Kalender wird geladen …" />
  const days = state.data.days
  if (days.length < 20) return null
  const m = metrics[metric]
  const years = [...new Set(days.map(d => d.day.slice(0, 4)))]
  const values = days.map(d => m.pick(d)).filter((v): v is number => v != null)
  const max = metric === 'autarky' ? 100 : Math.max(1, ...values.slice().sort((a, b) => a - b).slice(0, Math.ceil(values.length * .98)))
  const flagged = days.filter(d => d.flag)
  return <>
    <div className="row"><div className="segmented" role="group" aria-label="Kennzahl im Kalender">
      {(Object.keys(metrics) as (keyof typeof metrics)[]).map(k => <button key={k} aria-pressed={metric === k} onClick={() => setMetric(k)}>{metrics[k].label}</button>)}
    </div></div>
    <Chart height={years.length * 165 + 70} label={'Kalender ' + m.label} click={name => /^\d{4}-\d{2}-\d{2}$/.test(name) && drill(name)} option={{
      animation: false,
      tooltip: { renderMode: 'richText', formatter: (x: { value: [string, number] | [string, number, string] }) =>
        date(x.value[0]) + ' · ' + (x.value.length === 3 ? x.value[2] : number(x.value[1], 1) + ' ' + m.unit) },
      visualMap: { min: 0, max, calculable: false, orient: 'horizontal', left: 'center', bottom: 0, itemHeight: 140,
        inRange: { color: m.colors as unknown as string[] }, text: [number(max, 0) + ' ' + m.unit, '0'], seriesIndex: years.map((_, i) => i) },
      calendar: years.map((y, i) => ({ top: 30 + i * 165, left: 46, right: 12, cellSize: ['auto', 15], orient: 'horizontal',
        range: y,
        dayLabel: { firstDay: 1, nameMap: ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'], fontSize: 10 },
        monthLabel: { nameMap: ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'] },
        yearLabel: { show: true, position: 'left', margin: 30 }, splitLine: { show: false },
        itemStyle: { borderWidth: 2, borderColor: 'transparent', color: 'rgba(127,127,127,.12)' } })),
      series: [
        ...years.map((y, i) => ({ type: 'heatmap', coordinateSystem: 'calendar', calendarIndex: i,
          data: days.filter(d => d.day.startsWith(y) && !d.flag).flatMap(d => { const v = m.pick(d); return v == null ? [] : [[d.day, v]] }) })),
        ...years.map((y, i) => ({ type: 'scatter', coordinateSystem: 'calendar', calendarIndex: i, symbol: 'diamond', symbolSize: 9,
          itemStyle: { color: '#5c7065' }, data: flagged.filter(d => d.day.startsWith(y)).map(d => [d.day, 0,
            d.flag === 'outage' ? 'Ausfall, Werte am Folgetag nachgeliefert' : 'Nachholtag mit Energie mehrerer Tage']) })),
      ],
    }} />
    <p><small>Leere Felder: keine Werte. ◆ Ausfall- oder Nachholtag (Energie mehrerer Tage an einem Tag). Ein Klick öffnet den Tag.</small></p>
  </>
}

type MonthlyCost = { month: string; days: number; pricedDays: number; load: number; pv: number; import: number; export: number;
  importCost: number; exportRevenue: number; fixed: number; net: number; withoutPv: number; withoutBattery: number;
  pvSavings: number; batterySavings: number }
type CostResult = { months: MonthlyCost[]; total: MonthlyCost; hasImportPrice: boolean; hasExportPrice: boolean;
  days: number; pricedDays: number; notes: string[] }
const eur = (v: number | null | undefined, digits = 0) => v == null ? '—' : number(v, digits) + ' €'

/** Costs and savings per month from daily values and the stored tariffs. */
export function Costs({ query, openSettings }: { query: string; openSettings: () => void }) {
  const state = useData<{ result: CostResult }>('/economics?' + query)
  if (!state?.data) return <Status error={state?.error} text="Kosten werden berechnet …" />
  const r = state.data.result
  if (!r.hasImportPrice) return <Panel title="Kosten und Ersparnis">
    <p>Für die Kosten fehlen Tarife. Trage in den Einstellungen deinen Arbeitspreis, die Einspeisevergütung und den monatlichen Grundpreis
      ein, bei Preisänderungen mit Gültigkeitsbeginn. Danach rechnet die App für jeden Tag mit dem gültigen Preis.</p>
    <button onClick={openSettings}>Tarife erfassen</button>
  </Panel>
  const t = r.total
  let cumulative = 0
  const cum = r.months.map(m => (cumulative += m.pvSavings))
  return <Panel title="Kosten und Ersparnis">
    <div className="tiles">
      <Tile label="Stromkosten netto" value={eur(t.net)} icon="money" tone={tones.money}
        note={'Bezug ' + eur(t.importCost) + ' + Grundpreis ' + eur(t.fixed) + ' − Erlös ' + eur(t.exportRevenue)} />
      <Tile label="Ersparnis durch PV und Speicher" value={eur(t.pvSavings)} icon="pv" tone={tones.pv}
        note={'Ohne Anlage ' + eur(t.withoutPv)} info="Vergleich mit dem Fall, dass der gesamte Hausverbrauch aus dem Netz käme und nichts eingespeist würde. Grundpreis in beiden Fällen gleich." />
      <Tile label="davon durch den Speicher" value={eur(t.batterySavings)} icon="battery" tone={tones.battery}
        note={t.withoutBattery ? 'Ohne Speicher ' + eur(t.withoutBattery) : undefined}
        info="Ohne Speicher wäre die Entladung aus dem Netz bezogen und die Ladung eingespeist worden. Nur Tage mit Batteriewerten zählen." />
      <Tile label="Mittlerer Strompreis" value={t.import > 0 ? number(100 * (t.importCost + t.fixed) / t.import, 1) + ' ct/kWh' : '—'} icon="grid" tone={tones.grid}
        note="Bezugskosten inklusive Grundpreis je kWh aus dem Netz" />
    </div>
    {r.pricedDays < r.days && <p className="notice">Für {r.days - r.pricedDays} von {r.days} Tagen fehlt ein gültiger Bezugspreis. Diese Tage sind in den Beträgen nicht enthalten.</p>}
    {!r.hasExportPrice && <p className="notice">Es ist keine Einspeisevergütung hinterlegt. Erlöse werden mit 0 € gerechnet.</p>}
    <Chart label="Kosten und Ersparnis je Monat" option={{
      animation: false, legend: { type: 'scroll', top: 0 },
      tooltip: { trigger: 'axis', renderMode: 'richText', valueFormatter: (v: unknown) => typeof v === 'number' ? eur(v, 2) : '—' },
      grid: { left: 60, right: 64, top: 52, bottom: r.months.length > 24 ? 70 : 45 },
      xAxis: { type: 'category', data: r.months.map(m => m.month) },
      yAxis: [{ type: 'value', name: '€ je Monat' }, { type: 'value', name: '€ kumuliert' }],
      dataZoom: r.months.length > 24 ? [{ type: 'inside' }, { type: 'slider', height: 18 }] : [],
      series: [
        { name: 'Netzbezug', type: 'bar', stack: 'cost', data: r.months.map(m => m.importCost), itemStyle: { color: tones.grid } },
        { name: 'Grundpreis', type: 'bar', stack: 'cost', data: r.months.map(m => m.fixed), itemStyle: { color: '#c79aa6' } },
        { name: 'Einspeiseerlös', type: 'bar', stack: 'cost', data: r.months.map(m => -m.exportRevenue), itemStyle: { color: tones.export } },
        { name: 'Ersparnis kumuliert', type: 'line', yAxisIndex: 1, data: cum, itemStyle: { color: tones.pv }, lineStyle: { width: 3 }, showSymbol: false },
      ] }} />
    <details><summary>Monate als Tabelle</summary><div className="table"><table><thead><tr><th>Monat</th><th>Netzbezug</th><th>Einspeisung</th>
      <th>Kosten netto</th><th>Ohne Anlage</th><th>Ersparnis</th><th>davon Speicher</th></tr></thead><tbody>
      {r.months.map(m => <tr key={m.month}><td>{m.month}</td><td>{number(m.import, 0)} kWh</td><td>{number(m.export, 0)} kWh</td>
        <td>{eur(m.net, 2)}</td><td>{eur(m.withoutPv, 2)}</td><td>{eur(m.pvSavings, 2)}</td><td>{eur(m.batterySavings, 2)}</td></tr>)}
    </tbody></table></div></details>
    <details><summary>Rechenweg</summary><ul>{r.notes.map(n => <li key={n}>{n}</li>)}</ul></details>
  </Panel>
}

type Year = { year: number; days: number; charge: number; discharge: number; efficiency: number | null; cycles: number | null;
  cyclesPerYear: number | null; loadShare: number | null; fullDays: number; maxCharge: number | null }
type Month = { month: number; days: number; averageDischarge: number; efficiency: number | null; fullShare: number;
  dischargeByYear: Record<string, number>; efficiencyByYear: Record<string, number | null> }
type Health = { years: Year[]; months: Month[]; capacity: number | null; capacityEstimated: boolean; totalCycles: number;
  days: number; first: string | null; findings: string[]; notes: string[] }
const monthNames = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez']

/** Battery use and efficiency over the years, from daily charge and discharge. */
export function BatteryHealth() {
  const state = useData<{ result: Health }>('/battery-health')
  if (!state?.data) return <Status error={state?.error} text="Speicherzustand wird ausgewertet …" />
  const r = state.data.result
  if (!r.years.length) return <Panel title="Speicherzustand"><p>Es liegen keine Tageswerte für Batterieladung und -entladung vor.</p></Panel>
  const latest = r.years[r.years.length - 1]
  const years = r.years.map(y => String(y.year))
  const shades = ['#c9bfe3', '#a996d6', '#8f78c4', '#6d4dac', '#4b2f87']
  return <Panel title="Speicherzustand über die Jahre">
    <p className="lede">Seit {r.first ? date(r.first) : '—'} an {number(r.days, 0)} Tagen mit Batteriewerten. Kapazität
      {r.capacity == null ? ' unbekannt' : ' ' + number(r.capacity, 1) + ' kWh nutzbar' + (r.capacityEstimated ? ' (geschätzt, in den Einstellungen anpassbar)' : '')}.</p>
    <div className="tiles">
      <Tile label="Vollzyklen seit Beginn" value={number(r.totalCycles, 0)} icon="cycle" tone={tones.battery}
        note={latest.cyclesPerYear != null ? 'zuletzt etwa ' + number(latest.cyclesPerYear, 0) + ' pro Jahr' : undefined} />
      <Tile label={'Wirkungsgrad ' + latest.year} value={pct(latest.efficiency)} icon="ratio" tone={tones.export}
        info="Entladung ÷ Ladung. Enthält Lade- und Entladeverluste sowie den Eigenverbrauch des Speichers." />
      <Tile label={'Anteil am Hausverbrauch ' + latest.year} value={pct(latest.loadShare)} icon="house" tone={tones.house} />
      <Tile label={'Entladen ' + latest.year} value={number(latest.discharge, 0) + ' kWh'} icon="battery" tone={tones.battery}
        note={latest.days < 300 ? 'Teiljahr, ' + latest.days + ' Tage' : undefined} />
    </div>
    {!!r.findings.length && <ul className="findings">{r.findings.map(f => <li key={f}>{f}</li>)}</ul>}
    <Chart label="Entladung je Tag und Wirkungsgrad nach Monat" option={{
      animation: false, legend: { type: 'scroll', top: 0 },
      tooltip: { trigger: 'axis', renderMode: 'richText' },
      grid: { left: 56, right: 56, top: 52, bottom: 40 },
      xAxis: { type: 'category', data: monthNames },
      yAxis: [{ type: 'value', name: 'kWh je Tag' }, { type: 'value', name: 'Wirkungsgrad %', min: 70, max: 100 }],
      series: [
        ...years.map((y, i) => ({ name: 'Entladung ' + y, type: 'bar',
          itemStyle: { color: shades[Math.max(0, shades.length - years.length + i)] },
          tooltip: { valueFormatter: (v: unknown) => typeof v === 'number' ? number(v, 1) + ' kWh' : '—' },
          data: monthNames.map((_, m) => r.months.find(x => x.month === m + 1)?.dischargeByYear[y] ?? null) })),
        { name: 'Wirkungsgrad (alle Jahre)', type: 'line', yAxisIndex: 1, itemStyle: { color: tones.export }, lineStyle: { width: 3 },
          tooltip: { valueFormatter: (v: unknown) => typeof v === 'number' ? number(v, 1) + ' %' : '—' },
          data: monthNames.map((_, m) => { const e = r.months.find(x => x.month === m + 1)?.efficiency; return e == null ? null : e * 100 }) },
      ] }} />
    <div className="table"><table><thead><tr><th>Jahr</th><th>Tage</th><th>Geladen</th><th>Entladen</th><th>Wirkungsgrad</th>
      <th>Vollzyklen</th><th>Hochgerechnet pro Jahr</th><th>Tage voll geladen</th><th>Anteil am Verbrauch</th></tr></thead><tbody>
      {r.years.map(y => <tr key={y.year}><td>{y.year}</td><td>{y.days}</td><td>{number(y.charge, 0)} kWh</td><td>{number(y.discharge, 0)} kWh</td>
        <td>{pct(y.efficiency)}</td><td>{number(y.cycles, 0)}</td><td>{number(y.cyclesPerYear, 0)}</td><td>{y.fullDays}</td><td>{pct(y.loadShare)}</td></tr>)}
    </tbody></table></div>
    <details><summary>Rechenweg und Grenzen</summary><ul>{r.notes.map(n => <li key={n}>{n}</li>)}</ul></details>
  </Panel>
}
