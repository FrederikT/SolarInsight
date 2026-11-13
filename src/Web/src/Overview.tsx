import { useState } from 'react'
import { EnergyInsights } from './EnergyInsights'
import { base, useData, type EnergyEvent, type Job, type Point, type Report } from './api'
import { bounds, Chart, chartOptions, date, eventBounds, localDay, number, Panel, pct, power, stamp, Status } from './components'
import { TodayTimeline } from './LiveView'
import { LongTermCharts, PeriodHistory } from './PeriodCharts'
import type { Metrics } from './api'

const labels = { pv: 'PV-Erzeugung', load: 'Hausverbrauch', import: 'Netzbezug', export: 'Einspeisung',
  charge: 'Batterieladung', discharge: 'Batterieentladung', soc: 'Ladezustand', cost: 'Netto-Kosten' }
type Key = keyof typeof labels
const energy = ['pv', 'load', 'import', 'export'] as const
const storage = ['charge', 'discharge', 'soc'] as const
const keys = [...energy, ...storage]

export function Overview({ query, drill, day, evidence, simulation, version, liveVersion }: {
  query: string; drill: (day: string, exclusive?: string | null) => void; day: string | null; evidence: boolean;
  simulation: string | null; version: number; liveVersion: number
}) {
  const [perspective, setPerspective] = useState('energy')
  const [compare, setCompare] = useState('')
  const [model, setModel] = useState(false)
  const [heat, setHeat] = useState('')
  const [intervalSource, setIntervalSource] = useState(false)
  const path = query + (compare ? '&compare=' + compare : '')
  const state = useData<{ report: Report; comparison: Report | null; buckets: Point[]; providerBuckets?: Point[] | null }>('/overview?' + path, version)
  const events = useData<{ events: EnergyEvent[]; total: number; intervals: number; batteryEstimated: boolean;
    observations: { kind: string; text: string; day: string | null }[] }>('/events?' + query + '&limit=6', version)
  const modeled = useData<Job>(model && simulation && day ? '/simulations/' + simulation + '?day=' + day : null, version)
  const heatmap = useData<{ points: { day: string; hour: number; offset: string; import: number | null; export: number | null }[] }>(
    heat && !day ? '/heatmap?' + query : null, version)
  if (!state?.data) return <Status error={state?.error} />
  const { report, comparison, providerBuckets } = state.data
  const singleDay = Date.parse(report.period.end) - Date.parse(report.period.start) === 86_400_000
  const runningDay = singleDay && report.period.start === localDay(new Date().toISOString(), report.timeZone)
  const providerChart = !singleDay && perspective !== 'cost' && !!providerBuckets?.length && !intervalSource
  const buckets = providerChart ? providerBuckets! : state.data.buckets
  const m = report.metrics
  const dailyMetrics = report.providerMetrics
  /**
   * Picks the ratio with the widest paired basis (daily values or intervals). The ratio is shown whenever both
   * quantities exist on at least one day; a partial basis is stated instead of hiding the value.
   */
  function ratioKpi(kind: 'autarky' | 'retention', total: 'pv' | 'load', deducted: 'export' | 'import', definition: string) {
    const coverageKey = kind === 'autarky' ? 'autarkyCoverage' : 'retentionCoverage'
    const candidates = ([[dailyMetrics, 'Sungrow-Tageswerte'], [m, 'Messintervalle']] as [Metrics | null, string][])
      .filter(([x]) => x?.[kind] != null) as [Metrics, string][]
    const [best, source] = candidates.sort((a, b) => (b[0][coverageKey] ?? 0) - (a[0][coverageKey] ?? 0))[0] ?? []
    if (!best) return { value: null, basis: null, detail: ratioReason(total, deducted) }
    const basis = best[coverageKey] ?? 0
    return { value: best[kind], basis, detail: definition + ' Quelle: ' + source + '. Basis: ' + pct(basis) +
      ' des bisherigen Zeitraums mit beiden Werten' + (report.incomplete ? ' · laufender Zeitraum' : '') + '.' }
  }
  function ratioReason(total: 'pv' | 'load', deducted: 'export' | 'import') {
    const missing = [total, deducted].filter(key => m[key].kwh == null && dailyMetrics?.[key].kwh == null)
    if (missing.length) return 'Keine Messwerte für ' + missing.map(key => labels[key]).join(' und ')
    if ((dailyMetrics?.[total].kwh ?? m[total].kwh) === 0) return labels[total] + ' beträgt 0 kWh'
    return 'Keine Tage, an denen beide Werte erfasst sind'
  }
  const autarky = ratioKpi('autarky', 'load', 'import', 'Autarkiegrad = (Hausverbrauch − Netzbezug) ÷ Hausverbrauch.')
  const retention = ratioKpi('retention', 'pv', 'export', 'Eigenverbrauchsquote = (PV-Ertrag − Einspeisung) ÷ PV-Ertrag, einschließlich Speicherladung und -verluste. PV-Basis ist der Sungrow-PV-Ertrag.')
  const cost = report.cost
  const seriesKeys = singleDay ? keys : perspective === 'energy' ? energy : perspective === 'storage' ? storage : ['cost'] as Key[]
  const chartNames = buckets.map(p => p.local || p.day || '')
  const unit = singleDay ? 'kW' : perspective === 'cost' ? 'EUR' : 'kWh'
  const chartSeries = seriesKeys.map(key => ({
    name: labels[key], type: singleDay || key === 'soc' ? 'line' : 'bar',
    yAxisIndex: singleDay && key === 'soc' ? 1 : 0,
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
  }), ['Autarkiegrad', pct(autarky.value), autarky.detail, autarky.basis != null && autarky.basis < .999 ? 'Basis ' + pct(autarky.basis) + ' des Zeitraums' : ''],
  ['Eigenverbrauchsquote', pct(retention.value), retention.detail, retention.basis != null && retention.basis < .999 ? 'Basis ' + pct(retention.basis) + ' des Zeitraums' : ''],
  ['Netto-Stromkosten', number(cost.net ?? (cost.importCoverage > 0 || cost.exportCoverage > 0 || cost.fixed > 0 ? cost.coveredNet : null)) + ' €',
    cost.complete ? 'Bezug und Grundpreis abzüglich Erlös' : 'Nur gedeckter Teilbetrag']]
  const intervalCoverage = report.resolutionMinutes == null ? 0 : m.coverage
  const incompleteChannels = keys.filter(key => key === 'soc' ? m.socMean == null : m[key].coverage < .999999)
  const heatPoints = heatmap?.data?.points ?? []
  const hasHeatValues = heatPoints.some(point => (heat === 'import' ? point.import : point.export) != null)
  const missingSeries = seriesKeys.filter(key => !buckets.some(point => point[key] != null))
  const heatDays = [...new Set(heatPoints.map(p => p.day))]
  const repeatedHours = new Set(heatPoints.filter((point, index) => index > 0 &&
    heatPoints[index - 1].day === point.day && heatPoints[index - 1].hour === point.hour).map(point => point.hour))
  const heatHour = (p: typeof heatPoints[number]) => String(p.hour).padStart(2, '0') + ':00' +
    (repeatedHours.has(p.hour) ? ' UTC' + (p.offset.startsWith('-') ? p.offset.slice(0, 6) : '+' + p.offset.slice(0, 5)) : '')
  const heatHours = [...new Set(heatPoints.map(heatHour))].sort()
  return <>
    <div className="section-heading"><div><span className="eyebrow">ENERGIEBILANZ</span><h1>{day ? 'Tagesdetail' : evidence ? 'Ereigniszeitraum' : 'Übersicht'}</h1>
      <p>{bounds(report.period)} · {report.timeZone}</p></div><a className="button secondary" href={base + '/exports/overview?' + query}>CSV exportieren</a></div>
    {runningDay && !dailyMetrics && <p className="notice">Der Tag läuft noch. Der Verlauf zeigt bereits gespeicherte Leistungsmessungen.
      Tagesenergie und Quoten benötigen Energieintervalle oder Sungrow-Tageswerte. Für Nettokosten sind zusätzlich Tarife erforderlich.</p>}
    {dailyMetrics && incompleteChannels.length > 0 && <aside className="history-warning" aria-label="Achtung: Lücken im Zeitverlauf">
      <strong>Achtung: Der Zeitverlauf ist unvollständig</strong>
      <p>Für {incompleteChannels.map(key => labels[key]).join(', ')} fehlen einzelne oder alle Zeitwerte.
        Tageswerte können trotzdem vollständig sein. Für die Lücken braucht der Import Messwerte im Zeitraster.</p>
    </aside>}
    <details className="data-notes"><summary><span aria-hidden="true">ⓘ </span>Hinweise zur Datengrundlage</summary>
      <ul><li>Gemeinsame Minutenabdeckung: {pct(intervalCoverage)} der Zeit mit PV und Hausverbrauch im selben
        Messintervall. Sie zeigt, wie viel des Verlaufs sich zeitlich vergleichen lässt.</li>
        {dailyMetrics && <li>Gemeinsame Tagesabdeckung: {pct(dailyMetrics.coverage)} der Tage mit beiden Tagesenergien.
          Das reicht für Tagesvergleiche, auch wenn der Verlauf innerhalb eines Tages Lücken hat.</li>}
        {report.warnings.filter(w => !w.startsWith('Teildaten.') || !dailyMetrics || dailyMetrics.coverage < .999999)
        .map(w => <li key={w}>{w.startsWith('Zeitreihenenergie aus Momentanwerten')
          ? 'Zeitreihenenergie ist geschätzt. Die App verbindet benachbarte Leistungsmessungen und berechnet daraus die Energie im Intervall.'
          : w.startsWith('Sungrow-Tagesaggregate')
            ? 'Sungrow liefert eigene Tageswerte. Die App weist sie getrennt von den aus Minutenmessungen berechneten Werten aus.' : w}</li>)}
        {dailyMetrics && dailyMetrics.coverage >= .999999 && (report.resolutionMinutes == null || m.coverage < .999999) &&
          <li>PV und Hausverbrauch liegen für alle gewählten Tage als Tageswerte vor. Im Minutenverlauf fehlen gemeinsame Messintervalle.</li>}</ul>
    </details>
    {report.providerMetrics && <Panel title="Sungrow-Tagesaggregate"><p>PV {number(report.providerMetrics.pv.kwh)} kWh · Verbrauch {number(report.providerMetrics.load.kwh)} kWh · Bezug {number(report.providerMetrics.import.kwh)} kWh · Einspeisung {number(report.providerMetrics.export.kwh)} kWh.</p>
      <p>Batterieladung {number(report.providerMetrics.charge.kwh)} kWh · Batterieentladung {number(report.providerMetrics.discharge.kwh)} kWh.</p>
      {report.providerExportDifference != null && <p>Abweichung beim Export, Provider minus Zeitreihe · {number(report.providerExportDifference)} kWh.</p>}</Panel>}
    <div className="kpis">{kpis.map(([title, value, detail, basis]) => <article key={title}>
      <div className="kpi-label"><span>{title}</span><details className="kpi-info">
        <summary aria-label={'Details zu ' + title}>ⓘ</summary><p>{detail}</p>
      </details></div>
      <strong>{value}</strong>
      {value.startsWith('—') ? <small>Keine belastbare Angabe</small> : basis && <small>{basis}</small>}
    </article>)}</div>
    <Panel title={singleDay ? 'Gemessener Tagesverlauf' : 'Verlauf im Zeitraum'}>
      <div className="row">
        {!singleDay && <label>Perspektive<select value={perspective} onChange={e => setPerspective(e.target.value)}>
          <option value="energy">Energie und Speicher</option><option value="cost">Kosten</option></select></label>}
        <label>Vergleich<select value={compare} onChange={e => setCompare(e.target.value)}>
          <option value="">Aus</option><option value="previous">Vorperiode</option><option value="year">Vorjahr</option></select></label>
        {day && simulation && <label className="check"><input type="checkbox" checked={model} onChange={e => setModel(e.target.checked)} /> Szenario einblenden</label>}
        {!singleDay && perspective !== 'cost' && !!providerBuckets?.length && <label>Datenquelle<select value={intervalSource ? 'intervals' : 'daily'}
          onChange={event => setIntervalSource(event.target.value === 'intervals')}>
          <option value="daily">Sungrow-Tageswerte</option><option value="intervals">Summierte Messintervalle</option>
        </select></label>}
      </div>
      {modeled?.error && <Status error={modeled.error} />}
      {runningDay && <>
        <TodayTimeline zone={report.timeZone} version={liveVersion} label="Gespeicherte Messwerte des laufenden Tages" />
        <p>Vorläufige Energie für belegte Intervalle: {([...energy, 'charge', 'discharge'] as const)
          .filter(key => m[key].kwh != null)
          .map(key => labels[key] + ' ' + number(m[key].kwh) + ' kWh').join(' · ') ||
          'Für den laufenden Tag liegen noch keine belegten Energieintervalle vor.'}</p>
      </>}
      {!singleDay && perspective === 'energy' ? <PeriodHistory points={buckets} periodEnd={report.period.end} drill={drill} /> : <>
      {!runningDay && missingSeries.length > 0 && <p>Keine Werte im Verlauf für {missingSeries.map(key => labels[key]).join(', ')}.</p>}
      {(!runningDay || model) && (chartSeries.some(series => series.data.some(value => value != null))
        ? <Chart label={singleDay ? 'Tagesverlauf mit Leistung und Ladezustand' : 'Energieverlauf'} option={chartOptions(chartNames, unit, chartSeries, singleDay)}
          click={singleDay ? undefined : drill} /> : <Status text={singleDay && dailyMetrics ?
            'Für diesen Tag liegen Tageswerte vor. Ein Leistungsverlauf benötigt Minutenmesswerte.' :
            'Für diese Ansicht liegen im gewählten Zeitraum keine Daten vor.'} />)}</>}
      {comparison && <p>Vergleich {bounds(comparison.period)} · Netzbezug {number(comparison.metrics.import.kwh)} kWh · Einspeisung {number(comparison.metrics.export.kwh)} kWh · Netto {number(comparison.cost.net)} €{comparison.incomplete ? ' · laufend' : ''}{comparison.metrics.coverage < .999 ? ' · Teildaten' : ''}</p>}
      {m.socMean != null && <p>Ladezustand im Zeitraum · zeitgewichtet {pct(m.socMean)} · Minimum {pct(m.socMin)} · Maximum {pct(m.socMax)}.</p>}
      {perspective === 'cost' && <p>Bezug {number(cost.import)} € · Grundpreis {number(cost.fixed)} € · Erlös {number(cost.export)} € · Tarif-/Datenabdeckung Bezug {pct(cost.importCoverage)}, Export {pct(cost.exportCoverage)}</p>}
      {(singleDay || perspective !== 'energy') && <details><summary>{runningDay ? 'Historische Intervalle als Tabelle' : 'Diagrammdaten als Tabelle'}</summary><div className="table"><table><thead><tr><th>Zeit</th>{seriesKeys.map(key => <th key={key}>{labels[key]}{singleDay && key !== 'soc' ? '' : ' (' + (key === 'soc' ? '%' : unit) + ')'}</th>)}</tr></thead>
        <tbody>{buckets.map((p, i) => <tr key={p.start || p.day || i}><td>{p.day ? <button className="link" onClick={() => drill(p.day!)}>{date(p.day)}</button> : p.local}</td>
          {seriesKeys.map(key => <td key={key}>{key === 'soc' ? pct(p.soc) :
            singleDay && p[key] != null ? power(p[key]!) : number(p[key])}</td>)}</tr>)}</tbody></table></div></details>}
      {!singleDay && Date.parse(report.period.end) - Date.parse(report.period.start) >= 300 * 86_400_000 &&
        <LongTermCharts points={providerBuckets?.length ? providerBuckets : state.data.buckets} periodEnd={report.period.end} />}
    </Panel>
    {!!report.counterGaps?.length && <Panel title="Zählerenergie über Messlücken">
      <p>Die Summe gilt für das gesamte Zeitfenster und bleibt getrennt vom Viertelstundenprofil und dessen Energiesummen.</p>
      <details><summary>{report.counterGaps.length} belegte Zeitfenster anzeigen</summary><ul>
        {report.counterGaps.map(gap => <li key={gap.channel + gap.start}>{labels[gap.channel]} · {stamp(gap.start, report.timeZone)}
          {' '}bis {stamp(gap.end, report.timeZone)} · {number(gap.kwh, 3)} kWh</li>)}
      </ul></details>
    </Panel>}
    {!!report.historicalSoc?.length && <Panel title="Historischer Ladezustand">
      <p>{report.historicalSoc.length} Gerätezeitpunkte. Werte zwischen den Stichproben bleiben offen.</p>
      <Chart label="Historische Ladezustand-Stichproben" option={{
        animation: false, grid: { left: 58, right: 35, top: 24, bottom: 40 },
        xAxis: { type: 'time', axisLabel: { formatter: (at: number) =>
          new Date(at).toLocaleTimeString('de-DE', { timeZone: report.timeZone, hour: '2-digit', minute: '2-digit' }) } },
        yAxis: { type: 'value', min: 0, max: 100, axisLabel: { formatter: '{value} %' } },
        tooltip: { trigger: 'item', renderMode: 'richText',
          formatter: (point: { value: [number, number] }) =>
            stamp(new Date(point.value[0]).toISOString(), report.timeZone) + ' · ' + number(point.value[1], 1) + ' %' },
        series: [{ type: 'scatter', data: report.historicalSoc.map(sample => [Date.parse(sample.at), sample.value * 100]) }]
      }} />
    </Panel>}
    <EnergyInsights key={query} query={query} version={version} drill={drill} dailyCoverage={dailyMetrics?.coverage} />
    {!day && <Panel title="Tageszeit und Energie">
      <label>Heatmap<select value={heat} onChange={e => setHeat(e.target.value)}><option value="">Aus</option><option value="import">Netzbezug</option><option value="export">Einspeisung</option></select></label>
      <p>Die Spalten zeigen Kalendertage, die Zeilen lokale Uhrzeiten. Jede Zelle summiert die erfasste Energie der Stunde in kWh.
        Dunklere Farben bedeuten mehr Energie. Eine leere Zelle bedeutet fehlende Messintervalle. 0 kWh ist ein erfasster Wert.
        Bei Teildaten kann eine Stundensumme unter dem tatsächlichen Wert liegen.</p>
      {heat && !heatmap?.data && <Status error={heatmap?.error} />}
      {heat && heatmap?.data && !hasHeatValues && <Status text={'Keine Stundenwerte für ' + (heat === 'import' ? 'Netzbezug' : 'Einspeisung') + ' im gewählten Zeitraum.'} />}
      {hasHeatValues && <Chart label="Heatmap nach Tag, Stunde und UTC-Abstand" option={{
        animation: false, tooltip: { renderMode: 'richText', formatter: (item: { value: [number, number, number] }) => {
          const [dayIndex, hourIndex, value] = item.value
          return date(heatDays[dayIndex]) + ' · ' + heatHours[hourIndex] + ' · ' + number(value, 3) + ' kWh'
        } }, grid: { left: 125, right: 22, bottom: 75 },
        xAxis: { type: 'category', data: heatDays }, yAxis: { type: 'category', data: heatHours },
        visualMap: { min: 0, max: Math.max(1, ...heatPoints.map(p => (heat === 'import' ? p.import : p.export) || 0)),
          inRange: { color: ['#e6f2eb', '#176548'] }, text: ['Mehr kWh', 'Weniger kWh'], orient: 'horizontal', bottom: 5 },
        series: [{ type: 'heatmap', data: heatPoints.flatMap(p => {
          const value = heat === 'import' ? p.import : p.export
          return value == null ? [] : [[heatDays.indexOf(p.day), heatHours.indexOf(heatHour(p)), value]]
        }) }],
      }} />}
    </Panel>}
    <Panel title={day ? 'Ereignisse am Tag' : 'Beobachtungen im Zeitraum'}>
      {!events?.data ? <Status error={events?.error} /> : <>
        {!!events.data.observations.length && !day && <><h3>Aus Tageswerten</h3><ul>{events.data.observations.map(o => <li key={o.kind}>
          {o.day ? <><button className="link" onClick={() => drill(o.day!)}>{date(o.day)}</button> · </> : null}{o.text}</li>)}</ul></>}
        <h3>Aus Messintervallen</h3>
        {events.data.events.length ? <><ul>{events.data.events.map(e => <li key={e.type + e.start}>
          <button className="link" onClick={() => { const p = eventBounds(e.start, e.end, report.timeZone); drill(p.first, p.exclusive) }}>{stamp(e.start, report.timeZone)}</button> · {e.text}
        </li>)}</ul>{events.data.total > events.data.events.length && <p><small>{events.data.total} Ereignisse insgesamt. Alle stehen unter Speicher &amp; Potenzial.</small></p>}</> :
          <p>{events.data.intervals === 0 ? 'Für diesen Zeitraum gibt es keine 15-Minuten-Intervalle. Ereignisse wie Überschussepisoden oder Netzbezug an der Ladegrenze brauchen zeitlich aufgelöste Messwerte.'
            : 'In den vorhandenen Intervallen wurde keine Ereignisschwelle erreicht.'}</p>}
        {events.data.batteryEstimated && <p><small>Speichergrenzen aus dem beobachteten Ladezustand abgeleitet, weil keine Speicherparameter hinterlegt sind.</small></p>}
      </>}
    </Panel>
  </>
}

