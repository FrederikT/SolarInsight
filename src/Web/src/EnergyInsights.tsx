import { useEffect, useRef, useState } from 'react'
import { useData } from './api'
import { Chart, chartOptions, colors, date, localDay, number, Panel, pct, stamp, Status } from './components'

type Ring = { total: number; direct: number; battery: number; grid: number }
type Frequency = { count: number; eligible: number }
type Limit = { hours: number; eligibleHours: number; kwh: number; gridEligibleHours: number; atLimitGridHours: number }
type Hour = { hour: number; usableSoc: number | null; socDays: number; import: Frequency; empty: Frequency;
  full: Frequency; emptyImport: Frequency; fullExport: Frequency; emptyAmongImports: Frequency }
type HourDay = { day: string; usableSoc: number | null; import: boolean | null; empty: boolean | null; full: boolean | null;
  emptyImport: boolean | null; fullExport: boolean | null; expectedHours: number }
type Point = { start: string; end: string; directKw: number | null; batteryKw: number | null; gridKw: number | null;
  importKw: number | null; exportKw: number | null; soc: number | null; limit: string | null; mix: number | null }
type Insights = { period: { start: string; end: string; days: number }; zone: string; hours: number; start: string; end: string;
  latest: string | null; points: Point[]; breakdown: { household: Ring | null; production: Ring | null; validHours: number;
    missingHours: number; rejectedHours: number; rejectedDifferenceKwh: number };
  storage: { lower: Limit; upper: Limit; hours: Hour[]; days: HourDay[] };
  totals: { mix: number; hours: number; kwh: number }[]; phases: { start: string; end: string; kwh: number }[]; phaseCount: number;
  nights: { evening: string; emptyHours: number | null; complete: boolean; firstPv: string | null }[] }
const mixLabels = ['Nur Netz', 'Netz + PV', 'Netz + Speicher', 'Netz + PV + Speicher']
const hourLabel = (hour: number) => String(hour).padStart(2, '0') + ':00'
const hourRange = (hour: number) => hourLabel(hour) + '–' + hourLabel(hour + 1)
const frequency = (value: Frequency) => value.eligible ? value.count + ' von ' + value.eligible : 'Keine auswertbaren'
const ratio = (value: Frequency) => value.eligible ? 100 * value.count / value.eligible : null
const flag = (value: boolean | null) => value == null ? 'Unbekannt' : value ? 'Ja' : 'Nein'

function EnergyRing({ title, ring, labels }: { title: string; ring: Ring | null; labels: string[] }) {
  const data = ring ? [ring.direct, ring.battery, ring.grid] : []
  return <Panel title={title}>
    {!ring ? <p>Aufteilung mangels bestätigter, plausibler Energieflüsse nicht verfügbar.</p> : !ring.total ?
      <p>0 kWh in den auswertbaren Intervallen.</p> : <>
        <Chart label={title} option={{ animation: false, tooltip: { trigger: 'item', renderMode: 'richText',
          formatter: (p: { name: string; value: number }) => p.name + ' · ' + number(p.value) + ' kWh · ' + pct(p.value / ring.total) },
          series: [{ type: 'pie', radius: ['58%', '83%'], label: { show: false },
            data: data.map((value, i) => ({ value, name: labels[i] })) }],
          graphic: [{ type: 'text', left: 'center', top: 'middle', style: {
            text: number(ring.total) + ' kWh', fontSize: 21, fontWeight: 'bold', fill: '#1a3027' } }] }} />
        <ul>{data.map((value, i) => <li key={labels[i]}><span aria-hidden="true" className="energy-swatch" style={{ background: colors[i] }} />{labels[i]} · {number(value)} kWh · {pct(value / ring.total)}</li>)}</ul>
        {Math.abs(ring.total - data.reduce((sum, v) => sum + v, 0)) > .000001 &&
          <p>Bilanzrest innerhalb der Messtoleranz · {number(ring.total - data.reduce((sum, v) => sum + v, 0), 4)} kWh</p>}
      </>}
  </Panel>
}

function DayPlots({ data }: { data: Insights }) {
  const fmt = new Intl.DateTimeFormat('de-DE', { timeZone: data.zone, hour: '2-digit', minute: '2-digit', timeZoneName: 'shortOffset' })
  const base = { animation: false, legend: { type: 'scroll' }, tooltip: { trigger: 'axis', renderMode: 'richText',
    axisPointer: { label: { formatter: (p: { value: number }) => fmt.format(p.value) } } },
    grid: { left: 56, right: 58, top: 50, bottom: 46 },
    xAxis: { type: 'time', min: Date.parse(data.start), max: Date.parse(data.end), axisLabel: { formatter: (v: number) => fmt.format(v) } } }
  const series = (key: 'directKw' | 'batteryKw' | 'gridKw' | 'importKw' | 'exportKw' | 'soc', name: string) => {
    const values: [number, number | null][] = []
    data.points.forEach((p, i) => {
      const v = p[key]
      values.push([Date.parse(p.start), key === 'soc' && v !== null ? v * 100 : v])
      if (v !== null) values.push([Date.parse(p.end) - 1, key === 'soc' ? v * 100 : v])
      if (data.points[i + 1]?.start !== p.end) values.push([Date.parse(p.end), null])
    })
    return { name, type: 'line', step: 'end', showSymbol: false, connectNulls: false, data: values }
  }
  const marks = data.points.filter(p => p.limit === 'lower' || p.limit === 'upper').map(p => [
    { xAxis: Date.parse(p.start), itemStyle: { color: p.limit === 'lower' ? 'rgba(168,50,78,.19)' : 'rgba(0,130,105,.19)' } },
    { xAxis: Date.parse(p.end) },
  ])
  return <>
    <Panel title="Haushaltslast nach Quelle">
      {data.points.some(p => p.directKw !== null) ? <Chart label="Gestapelte Haushaltslast aus PV, Speicher und Netz" option={{
        ...base, yAxis: { type: 'value', name: 'kW' }, series: [series('directKw', 'Direkt aus PV'), series('batteryKw', 'Aus dem Speicher'),
          series('gridKw', 'Aus dem Netz')].map(s => ({ ...s, stack: 'load', areaStyle: { opacity: .75 } })),
      }} /> : <p>Keine belastbare Zuordnung der Haushaltslast.</p>}
    </Panel>
    <Panel title="Speicherzustand und Netzleistung am Tag">
      {data.points.length ? <Chart label="Gemeldeter SOC und Netzleistung auf lokaler Uhrzeitachse" option={{
        ...base, yAxis: [{ type: 'value', name: 'kW' }, { type: 'value', name: 'Gemeldeter SOC (%)', min: 0, max: 100 }],
        series: [series('importKw', 'Netzbezug'), series('exportKw', 'Einspeisung'),
          { ...series('soc', 'Gemeldeter SOC'), yAxisIndex: 1, markArea: { silent: true, data: marks } }],
      }} /> : <p>Keine Zeitreihen für diesen Tag.</p>}
      <p>Rote Abschnitte markieren die untere, grüne die obere nutzbare Ladegrenze. Die Kurve zeigt den gemeldeten SOC.</p>
      {data.points.some(p => p.mix !== null) && <details><summary>Netzbezugsintervalle am Tag</summary><ul>
        {data.points.filter(p => p.mix !== null).map(p => <li key={p.start}>{stamp(p.start, data.zone)} · {mixLabels[p.mix!]} · {number(p.gridKw)} kW</li>)}
      </ul></details>}
    </Panel>
  </>
}

export function EnergyInsights({ query, version, drill }: { query: string; version: number; drill: (day: string) => void }) {
  const [hour, setHour] = useState<number | null>(null)
  const [mix, setMix] = useState<number | null>(null)
  const [offset, setOffset] = useState(0)
  const [pure, setPure] = useState(false)
  const evidence = useRef<HTMLDivElement>(null)
  useEffect(() => { if (hour !== null) evidence.current?.scrollIntoView({ block: 'nearest' }) }, [hour])
  const path = '/energy-insights?' + query
  const summary = useData<Insights>(path, version)
  const detail = useData<Insights>(hour === null ? null : path + '&hour=' + hour, version)
  const phases = useData<Insights>(mix === null ? null : path + '&mix=' + mix + '&offset=' + offset, version)
  if (!summary?.data) return <Status error={summary?.error} text="Energieflüsse und Speicherzustand werden ausgewertet …" />
  const data = summary.data
  const { breakdown, storage } = data
  const multi = data.period.days > 1
  const night = [22, 23, 0, 1, 2, 3, 4, 5].map(h => storage.hours[h])
  const chooseHour = (label: string) => setHour(Number(label.slice(0, 2)))
  const first = pure ? 'empty' : 'emptyImport'
  const second = pure ? 'full' : 'fullExport'
  const firstLabel = pure ? 'Leer' : 'Leer + Netzbezug'
  const secondLabel = pure ? 'Voll' : 'Voll + Einspeisung'
  return <>
    <div className="energy-rings">
      <EnergyRing title="Woher kam mein Haushaltsstrom?" ring={breakdown.household} labels={['Direkt aus PV', 'Aus dem Speicher', 'Aus dem Netz']} />
      <EnergyRing title="Wohin ging mein PV-Ertrag?" ring={breakdown.production} labels={['Direkt im Haushalt genutzt', 'In den Speicher geladen', 'Ins Netz eingespeist']} />
    </div>
    <p>Gemeinsam bilanzierbar · {number(breakdown.validHours)} von {number(data.hours)} Stunden ({pct(breakdown.validHours / data.hours)}).
      {data.latest && <> Letzter abgedeckter Tag · {date(localDay(new Date(Date.parse(data.latest) - 1).toISOString(), data.zone))}.</>}
      {breakdown.rejectedHours > 0 && <> Ausgeschlossen wegen Bilanzabweichung · {number(breakdown.rejectedHours)} h, {number(breakdown.rejectedDifferenceKwh)} kWh absolute Abweichung.</>}
      {' '}Null- und Bilanzschwelle · 0,05 kW je Messintervall.</p>
    <Panel title="Zeit an den nutzbaren Ladegrenzen">
      <dl className="facts">{([['Untere Ladegrenze', 'Netzbezug', storage.lower], ['Obere Ladegrenze', 'Einspeisung', storage.upper]] as const)
        .map(([title, grid, limit]) => <div key={title}><dt>{title}</dt><dd>{limit.eligibleHours ?
          number(limit.hours) + ' h · ' + pct(limit.hours / limit.eligibleHours) : 'Nicht auswertbar'}</dd>
          <small>{number(limit.eligibleHours)} h mit auswertbarem SOC und bestätigten Ladegrenzen</small>
          <dd>{grid} · {limit.atLimitGridHours || (limit.eligibleHours && !limit.hours) ? number(limit.kwh) + ' kWh' : 'nicht auswertbar'}</dd>
          <small>{number(limit.atLimitGridHours)} von {number(limit.hours)} h an der Ladegrenze mit gültigem Netzkanal</small></div>)}</dl>
    </Panel>
    {!multi ? <DayPlots data={data} /> : <>
      <Panel title="Wann brauchte ich Netzstrom?">
        <p>Die Quellen haben im selben Messintervall beigetragen. Die Einteilung beschreibt die Messung.</p>
        <div className="row">{mixLabels.map((name, i) => {
          const total = data.totals.find(t => t.mix === i)
          return <button className="secondary" aria-pressed={mix === i} key={name} disabled={!total}
            onClick={() => { setMix(i); setOffset(0) }}>{name} · {number(total?.hours ?? 0)} h · {number(total?.kwh ?? 0)} kWh</button>
        })}</div>
        {breakdown.validHours === 0 && <p>Keine bestätigten Energieflüsse für die Einteilung verfügbar.</p>}
        {mix !== null && (phases?.data ? <>
          <ul>{phases.data.phases.map(p => <li key={p.start}><button className="link" onClick={() => drill(localDay(p.start, data.zone))}>
            {stamp(p.start, data.zone)} bis {stamp(p.end, data.zone)}</button> · {number(p.kwh)} kWh</li>)}</ul>
          <div className="row"><button className="secondary" disabled={offset === 0} onClick={() => setOffset(offset - 40)}>Vorherige Abschnitte</button>
            <button className="secondary" disabled={offset + 40 >= phases.data.phaseCount} onClick={() => setOffset(offset + 40)}>Weitere Abschnitte</button></div>
        </> : <Status error={phases?.error} />)}
      </Panel>
      <Panel title="Wiederkehrender nächtlicher Netzbezug">
        <p>Nachtfenster 22:00 bis 06:00 Uhr. Wiederkehrender Bezug an der unteren Ladegrenze ist ein Hinweis für die Speichersimulation.
          Ob mehr Kapazität hilft, hängt auch vom PV-Überschuss zum Laden ab.</p>
        <Chart label="Nächtlicher Netzbezug entlang der Uhrzeit" click={chooseHour} option={chartOptions(night.map(h => hourRange(h.hour)), 'Nächte', [
          { name: 'Mit Netzbezug', data: night.map(h => h.import.eligible ? h.import.count : null) },
          { name: 'Netzbezug an der unteren Ladegrenze', data: night.map(h => h.emptyAmongImports.eligible ? h.emptyAmongImports.count : null) },
        ])} />
        <ul>{night.map(h => <li key={h.hour}><button className="link" onClick={() => setHour(h.hour)}>{hourRange(h.hour)}</button>
          {' '}· {frequency(h.import)} Nächten mit Netzbezug · {frequency(h.emptyAmongImports)} Nächten mit Netzbezug und auswertbarem SOC an der unteren Ladegrenze</li>)}</ul>
      </Panel>
      <Panel title="Typischer Tagesverlauf des Speichers">
        <p>Lokale Uhrzeit. Die Kurve zeigt den mittleren Anteil des nutzbaren Speicherfensters.
          Häufigkeiten zählen Tage mit mindestens einem passenden Intervall je auswertbarer Stunde.</p>
        <Chart label="Mittlerer SOC im nutzbaren Speicherfenster" click={chooseHour} option={chartOptions(storage.hours.map(h => hourLabel(h.hour)), '%', [
          { name: 'Nutzbares Speicherfenster', type: 'line', data: storage.hours.map(h => h.usableSoc === null ? null : 100 * h.usableSoc) },
        ])} />
        <label className="check"><input type="checkbox" checked={pure} onChange={e => setPure(e.target.checked)} /> Reine Speicherzustände anzeigen</label>
        <Chart label="Häufigkeit der Speicherzustände" click={chooseHour} option={chartOptions(storage.hours.map(h => hourLabel(h.hour)), '%', [
          { name: firstLabel, data: storage.hours.map(h => ratio(h[first])) }, { name: secondLabel, data: storage.hours.map(h => ratio(h[second])) },
        ])} />
        <details><summary>Uhrzeiten und Bezugsgrößen als Tabelle</summary><div className="table"><table><thead><tr>
          <th>Uhrzeit</th><th>SOC und Tage</th><th>{firstLabel}</th><th>{secondLabel}</th></tr></thead><tbody>
          {storage.hours.map(h => <tr key={h.hour}><td><button className="link" onClick={() => setHour(h.hour)}>{hourRange(h.hour)}</button></td>
            <td>{pct(h.usableSoc)} · {h.socDays}</td><td>{frequency(h[first])}</td><td>{frequency(h[second])}</td></tr>)}
        </tbody></table></div></details>
      </Panel>
      {hour !== null && <div ref={evidence}><Panel title={'Belegende Tage · ' + hourRange(hour)}>
        {detail?.data ? <>
          <div className="table"><table><thead><tr><th>Tag</th><th>Nutzbarer SOC</th><th>Netzbezug</th><th>{firstLabel}</th><th>{secondLabel}</th></tr></thead>
            <tbody>{detail.data.storage.days.map(d => <tr key={d.day}><td><button className="link" onClick={() => drill(d.day)}>{date(d.day)}</button>
              {d.expectedHours !== 1 && <> · {number(d.expectedHours)} h</>}</td>
              <td>{pct(d.usableSoc)}</td><td>{flag(d.import)}</td><td>{flag(d[first])}</td><td>{flag(d[second])}</td></tr>)}</tbody></table></div>
          {detail.data.nights.length > 0 && <><h3>Nächte mit Bezug in diesem Fenster</h3><ul>{detail.data.nights.map(n => <li key={n.evening}>
            Nacht ab <button className="link" onClick={() => drill(n.evening)}>{date(n.evening)}</button> · Untere Ladegrenze bis zur nächsten PV-Erzeugung ·{' '}
            {n.emptyHours === null ? 'unbekannt' : number(n.emptyHours) + ' h' + (n.complete ? '' : ' erfasst (unvollständig)')}
            {n.firstPv && <> · PV ab <button className="link" onClick={() => drill(localDay(n.firstPv!, data.zone))}>{stamp(n.firstPv, data.zone)}</button></>}
          </li>)}</ul></>}
        </> : <Status error={detail?.error} />}
      </Panel></div>}
    </>}
  </>
}
