import { useState } from 'react'
import { EnergyInsights } from './EnergyInsights'
import { base, useData, type EnergyEvent, type Job, type Point, type Report } from './api'
import { bounds, Chart, chartOptions, date, eventBounds, number, Panel, pct, stamp, Status } from './components'

const labels = { pv: 'PV-Erzeugung', load: 'Hausverbrauch', import: 'Netzbezug', export: 'Einspeisung',
  charge: 'Batterieladung', discharge: 'Batterieentladung', soc: 'Ladezustand', cost: 'Netto-Kosten' }
type Key = keyof typeof labels
const energy = ['pv', 'load', 'import', 'export'] as const
const storage: Key[] = ['charge', 'discharge', 'soc']
const keys = [...energy, ...storage]

export function Overview({ query, drill, day, evidence, simulation, version }: {
  query: string; drill: (day: string, exclusive?: string | null) => void; day: string | null; evidence: boolean;
  simulation: string | null; version: number
}) {
  const [perspective, setPerspective] = useState('energy')
  const [compare, setCompare] = useState('')
  const [model, setModel] = useState(false)
  const [heat, setHeat] = useState('')
  const path = query + (compare ? '&compare=' + compare : '')
  const state = useData<{ report: Report; comparison: Report | null; buckets: Point[] }>('/overview?' + path, version)
  const events = useData<{ events: EnergyEvent[] }>('/events?' + query + '&limit=6', version)
  const modeled = useData<Job>(model && simulation && day ? '/simulations/' + simulation + '?day=' + day : null, version)
  const heatmap = useData<{ points: { day: string; hour: number; offset: string; import: number | null; export: number | null }[] }>(
    heat && !day ? '/heatmap?' + query : null, version)
  if (!state?.data) return <Status error={state?.error} />
  const { report, comparison, buckets } = state.data
  const m = report.metrics
  const dailyMetrics = report.providerMetrics
  function dailyRatio(total: 'pv' | 'load', deducted: 'export' | 'import') {
    const a = dailyMetrics?.[total], b = dailyMetrics?.[deducted]
    return a?.kwh != null && a.kwh > 0 && b?.kwh != null && a.coverage >= .999999 && b.coverage >= .999999
      ? 1 - b.kwh / a.kwh : null
  }
  const estimatedAutarky = dailyRatio('load', 'import')
  const estimatedRetention = dailyRatio('pv', 'export')
  const cost = report.cost
  const seriesKeys = day ? keys : perspective === 'energy' ? energy : perspective === 'storage' ? storage : ['cost'] as Key[]
  const chartNames = buckets.map(p => p.local || p.day || '')
  const unit = day ? 'kW' : perspective === 'cost' ? 'EUR' : 'kWh'
  const chartSeries = seriesKeys.map(key => ({
    name: labels[key], type: day || key === 'soc' ? 'line' : 'bar',
    yAxisIndex: day && key === 'soc' ? 1 : 0,
    data: buckets.map(p => key === 'soc' && p.soc != null ? p.soc * 100 : p[key] ?? null),
    ...(day && key === 'export' && events?.data ? { markArea: { silent: true, itemStyle: { opacity: .14 },
      data: events.data.events.map(e => {
        const start = buckets.findIndex(p => p.start === e.start)
        const end = buckets.findIndex(p => p.start === e.end)
        return [{ xAxis: chartNames[Math.max(0, start)] }, { xAxis: chartNames[end < 0 ? chartNames.length - 1 : end] }]
      }) } } : {}),
  }))
  if (modeled?.data?.model?.length) {
    const points = new Map(modeled.data.model.map(p => [p.start, p]))
    for (const [key, label] of [['importKw', 'Netzbezug'], ['exportKw', 'Einspeisung'], ['loadKw', 'Verbrauch'], ['soc', 'SOC']] as const)
      chartSeries.push({ name: 'Modell · ' + label, type: 'line', yAxisIndex: key === 'soc' ? 1 : 0,
        data: buckets.map(b => { const value = points.get(b.start || '')?.[key]; return value == null ? null : key === 'soc' ? value * 100 : value }) })
  }
  const kpis = [...energy.map(key => {
    const daily = report.providerMetrics?.[key]
    const useDaily = daily?.kwh != null && daily.coverage >= m[key].coverage
    const channel = useDaily ? daily : m[key]
    return [labels[key], number(channel.kwh) + ' kWh',
      (useDaily ? 'Sungrow-Tageswerte · ' : '') + 'Abdeckung ' + pct(channel.coverage)]
  }), ['Autarkiegrad', pct(dailyMetrics?.autarky ?? m.autarky ?? estimatedAutarky),
    dailyMetrics?.autarky != null || m.autarky != null ? 'Anteil des erfassten Hausverbrauchs'
      : estimatedAutarky != null ? 'Näherung aus Tageswerten · setzt Betrieb ohne Netzladen voraus' : 'Benötigt Verbrauch und Netzbezug'],
  ['Eigenverbrauchsquote', pct(dailyMetrics?.retention ?? m.retention ?? estimatedRetention),
    dailyMetrics?.retention != null || m.retention != null ? 'Inkl. Speicherverluste und Bestandsänderung'
      : estimatedRetention != null ? 'Näherung aus Tageswerten · setzt reine PV-Einspeisung voraus' : 'Benötigt PV-Ertrag und Einspeisung'],
  ['Netto-Stromkosten', number(cost.net ?? (cost.importCoverage > 0 || cost.exportCoverage > 0 || cost.fixed > 0 ? cost.coveredNet : null)) + ' €',
    cost.complete ? 'Bezug und Grundpreis abzüglich Erlös' : 'Nur gedeckter Teilbetrag'],
  ['Datenabdeckung', pct(m.coverage), 'Gemeinsame PV- und Verbrauchsdaten']]
  const heatPoints = heatmap?.data?.points ?? []
  const heatDays = [...new Set(heatPoints.map(p => p.day))]
  const heatHours = [...new Set(heatPoints.map(p => String(p.hour).padStart(2, '0') + ':00 UTC+' + p.offset.slice(0, 5)))].sort()
  return <>
    <div className="section-heading"><div><span className="eyebrow">ENERGIEBILANZ</span><h1>{day ? 'Tagesdetail' : evidence ? 'Ereigniszeitraum' : 'Übersicht'}</h1>
      <p>{bounds(report.period)} · {report.timeZone}</p></div><a className="button secondary" href={base + '/exports/overview?' + query}>CSV exportieren</a></div>
    {report.warnings.map(w => <Status key={w} text={w} />)}
    {report.providerMetrics && <Panel title="Sungrow-Tagesaggregate"><p>PV {number(report.providerMetrics.pv.kwh)} kWh · Verbrauch {number(report.providerMetrics.load.kwh)} kWh · Bezug {number(report.providerMetrics.import.kwh)} kWh · Einspeisung {number(report.providerMetrics.export.kwh)} kWh.</p>
      <p>Batterieladung {number(report.providerMetrics.charge.kwh)} kWh · Batterieentladung {number(report.providerMetrics.discharge.kwh)} kWh.</p>
      {report.providerExportDifference != null && <p>Abweichung beim Export, Provider minus Zeitreihe · {number(report.providerExportDifference)} kWh.</p>}</Panel>}
    <div className="kpis">{kpis.map(([title, value, detail]) => <article key={title}><span>{title}</span><strong>{value}</strong><small>{detail}</small></article>)}</div>
    <Panel title={day ? 'Gemessener Tagesverlauf' : 'Verlauf im Zeitraum'}>
      <div className="row">
        {!day && <label>Perspektive<select value={perspective} onChange={e => setPerspective(e.target.value)}>
          <option value="energy">Energie</option><option value="cost">Kosten</option><option value="storage">Speicher</option></select></label>}
        <label>Vergleich<select value={compare} onChange={e => setCompare(e.target.value)}>
          <option value="">Aus</option><option value="previous">Vorperiode</option><option value="year">Vorjahr</option></select></label>
        {day && simulation && <label className="check"><input type="checkbox" checked={model} onChange={e => setModel(e.target.checked)} /> Szenario einblenden</label>}
      </div>
      {modeled?.error && <Status error={modeled.error} />}
      {buckets.some(b => b.pv != null || b.load != null || b.import != null || b.export != null)
        ? <Chart label={day ? 'Tagesverlauf mit Leistung und Ladezustand' : 'Energieverlauf'} option={chartOptions(chartNames, unit, chartSeries, !!day)}
          click={day ? undefined : drill} /> : <Status text="Für diesen Zeitraum liegen noch keine Messwerte vor." />}
      {comparison && <p>Vergleich {bounds(comparison.period)} · Netzbezug {number(comparison.metrics.import.kwh)} kWh · Einspeisung {number(comparison.metrics.export.kwh)} kWh · Netto {number(comparison.cost.net)} €{comparison.incomplete ? ' · laufend' : ''}{comparison.metrics.coverage < .999 ? ' · Teildaten' : ''}</p>}
      {perspective === 'storage' && <p>SOC zeitgewichtet {pct(m.socMean)} · Minimum {pct(m.socMin)} · Maximum {pct(m.socMax)}.</p>}
      {perspective === 'cost' && <p>Bezug {number(cost.import)} € · Grundpreis {number(cost.fixed)} € · Erlös {number(cost.export)} € · Tarif-/Datenabdeckung Bezug {pct(cost.importCoverage)}, Export {pct(cost.exportCoverage)}</p>}
      <details><summary>Diagrammdaten als Tabelle</summary><div className="table"><table><thead><tr><th>Zeit</th>{seriesKeys.map(key => <th key={key}>{labels[key]} ({key === 'soc' ? '%' : unit})</th>)}</tr></thead>
        <tbody>{buckets.map((p, i) => <tr key={p.start || p.day || i}><td>{p.day ? <button className="link" onClick={() => drill(p.day!)}>{date(p.day)}</button> : p.local}</td>
          {seriesKeys.map(key => <td key={key}>{key === 'soc' ? pct(p.soc) : number(p[key])}</td>)}</tr>)}</tbody></table></div></details>
    </Panel>
    <EnergyInsights key={query} query={query} version={version} drill={drill} />
    {!day && <Panel title="Tageszeit und Energie">
      <label>Heatmap<select value={heat} onChange={e => setHeat(e.target.value)}><option value="">Aus</option><option value="import">Netzbezug</option><option value="export">Einspeisung</option></select></label>
      {heat && !heatmap?.data && <Status error={heatmap?.error} />}
      {heatPoints.length > 0 && <Chart label="Heatmap nach Tag, Stunde und UTC-Abstand" option={{
        animation: false, tooltip: { renderMode: 'richText' }, grid: { left: 125, right: 22, bottom: 75 },
        xAxis: { type: 'category', data: heatDays }, yAxis: { type: 'category', data: heatHours },
        visualMap: { min: 0, max: Math.max(1, ...heatPoints.map(p => (heat === 'import' ? p.import : p.export) || 0)), orient: 'horizontal', bottom: 5 },
        series: [{ type: 'heatmap', data: heatPoints.flatMap(p => {
          const value = heat === 'import' ? p.import : p.export
          return value == null ? [] : [[heatDays.indexOf(p.day), heatHours.indexOf(String(p.hour).padStart(2, '0') + ':00 UTC+' + p.offset.slice(0, 5)), value]]
        }) }],
      }} />}
    </Panel>}
    <Panel title={day ? 'Ereignisse am Tag' : 'Beobachtungen im Zeitraum'}>
      {events?.data?.events.length ? <ul>{events.data.events.map(e => <li key={e.type + e.start}>
        <button className="link" onClick={() => { const p = eventBounds(e.start, e.end, report.timeZone); drill(p.first, p.exclusive) }}>{stamp(e.start, report.timeZone)}</button> · {e.text}
      </li>)}</ul> : <p>Keine belegten Ereignisse im gewählten Zeitraum.</p>}
    </Panel>
  </>
}

