import { useEffect, useState } from 'react'
import { request, useData } from './api'
import { Chart, colors, number, Panel, pct, power, stamp, Status } from './components'

const channels = [['pv', 'PV-Erzeugung (AC)'], ['load', 'Hausverbrauch'], ['grid', 'Netz'],
  ['battery', 'Batterie'], ['soc', 'Ladezustand']] as const
type Channel = typeof channels[number][0]
const timelineChannels = [...channels, ['pvDc', 'Solarleistung (DC)'], ['inverterActive', 'Wechselrichter-Wirkleistung']] as const
type TimelineChannel = typeof timelineChannels[number][0]
type Value = { value: number | null; direction: string; measuredAt: string | null; ageSeconds: number | null; quality: string }
type Now = Record<Channel, Value> & { pvSurplusKw: number | null; flowConfirmed: boolean; quality: string;
  pvDc?: Value | null; inverterActive?: Value | null;
  fetchedAt?: string | null; timing?: { overviewVisible: boolean; cloudIntervalSeconds: number; sourceMeasuredAt: string | null;
    nextEligibleAt: string | null; retryAt: string | null } }
type Today = { start: string; end: string; samples: { channel: TimelineChannel; at: string; end?: string | null; value: number; source: string }[];
  gaps: { channel: TimelineChannel; start: string; end: string; reason: string }[] }
const directions: Record<string, string> = { import: 'Netzbezug', export: 'Einspeisung', charge: 'Ladung',
  discharge: 'Entladung', idle: 'Keine Leistung', unknown: 'Unbekannt' }
const quality: Record<string, string> = { stale: 'veraltet', missing: 'Messwert fehlt', unconfigured: 'Zuordnung ausstehend',
  unavailable: 'Abruf nicht verfügbar', confirmed: 'bestätigt' }
const gapReasons: Record<string, string> = { 'poll-outage': 'Abrufunterbrechung', 'missing-channel': 'Messwert fehlt',
  'repeated-timestamp': 'Zeitstempel wiederholt' }

function tabId() {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-')
}

export function LiveView({ zone, version, refresh }: { zone: string; version: number; refresh: () => void }) {
  const [tab] = useState(tabId)
  const now = useData<Now>('/live/now', version)
  const [showToday, setShowToday] = useState(false)
  useEffect(() => {
    const signal = (visible: boolean) => { void request('/live/activity', 'POST', { tab, visible }).catch(() => {}) }
    const update = () => {
      const visible = document.visibilityState === 'visible'
      signal(visible)
      if (visible) refresh()
    }
    signal(document.visibilityState === 'visible')
    document.addEventListener('visibilitychange', update)
    const timer = window.setInterval(() => { if (document.visibilityState === 'visible') update() }, 30_000)
    return () => {
      signal(false)
      document.removeEventListener('visibilitychange', update)
      window.clearInterval(timer)
    }
  }, [tab, refresh])
  const state = now?.data
  const simultaneousExport = state && state.battery.value != null && state.battery.value < 0 &&
    state.load.value != null && -state.battery.value > state.load.value &&
    state.grid.value != null && state.grid.value < 0 && state.grid.measuredAt != null &&
    state.grid.measuredAt === state.battery.measuredAt && state.grid.measuredAt === state.load.measuredAt
  function value(channel: Channel) {
    const reading = state?.[channel]
    if (reading?.value == null) return reading ? quality[reading.quality] ?? 'Kein Messwert' : 'Wird geladen …'
    if (channel === 'soc') return reading.value === 0 ? 'Leer · 0 %' : pct(reading.value)
    return power(Math.abs(reading.value))
  }
  return <>
    <Panel title="Jetzt">
      {state?.quality === 'synthetic' && <p>Synthetische Live-Daten aus einem wiedergegebenen Demotag.</p>}
      {!state ? <Status error={now?.error} /> : <>
        {state.quality === 'unconfigured' && <p>Live-Messpunkte können in den Einstellungen geprüft und bestätigt werden.</p>}
        <div className="live-flow" aria-label="Aktuelle Energieflüsse">
          {channels.map(([key, title]) => key === 'pv' && state.pv.quality === 'unconfigured' && state.pvDc ? (
            <div className="live-pv" key={key}><div className="kpi-label"><strong>Solarleistung</strong>
              <details className="kpi-info"><summary aria-label="Quelle der Solarleistung">
                <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="2" /><path d="M12 11v6m0-10v2" stroke="currentColor" strokeWidth="2" /></svg>
              </summary><p>PV-DC-Leistung am Wechselrichter · AC-PV-Fluss nicht separat gemessen</p></details></div>
              <span>{state.pvDc.value == null ? quality[state.pvDc.quality] ?? 'Kein Messwert' : power(state.pvDc.value)}</span>
              <small>{state.pvDc.measuredAt && stamp(state.pvDc.measuredAt, zone)}</small>
            </div>
          ) : <div className={'live-' + key} key={key}>
            <strong>{key === 'pv' ? 'Solarleistung' : title}</strong><span>{value(key)}</span>
            <small>{state.flowConfirmed && state[key].value !== 0 &&
              (key === 'pv' || key === 'grid' && state.grid.direction === 'import' ||
                key === 'battery' && state.battery.direction === 'discharge') ? '→ Anlage · ' :
              state.flowConfirmed && state[key].value !== 0 && (key === 'grid' || key === 'battery') ? '← Anlage · ' : ''}
              {key !== 'soc' && state[key].value !== null ? directions[state[key].direction] : null}
              {state[key].measuredAt && <> {stamp(state[key].measuredAt!, zone)}
                {state[key].ageSeconds != null && <> · {number(Math.floor(state[key].ageSeconds! / 60))} Min. alt</>}</>}</small>
          </div>)}
        </div>
        {simultaneousExport && state?.grid.value != null && <p>Batterie entlädt · gleichzeitig werden {
          power(-state.grid.value)} ins Netz eingespeist.</p>}
        {(state.pvDc || state.inverterActive) && <dl className="facts" aria-label="Weitere Messwerte am Wechselrichter">
          {([['PV-DC-Leistung am Wechselrichter', state.pv.quality !== 'unconfigured' ? state.pvDc : null],
            ['Wechselrichter-Wirkleistung', state.inverterActive]] as const)
            .map(([label, reading]) => reading && <div key={label}><dt>{label}</dt>
              <dd>{reading.value == null ? quality[reading.quality] ?? 'Kein Messwert' : power(reading.value)}
                {reading.measuredAt && <> · {stamp(reading.measuredAt, zone)}</>}</dd></div>)}
        </dl>}
        {state.pvSurplusKw != null && <p>Bestätigter PV-Überschuss {power(state.pvSurplusKw)}.</p>}
        {state.timing && <details><summary>Messalter und Abrufstatus</summary>
          <dl className="facts">
            <div><dt>Letzte Quellmessung</dt><dd>{state.timing.sourceMeasuredAt ? stamp(state.timing.sourceMeasuredAt, zone) : 'Noch keine'}</dd></div>
            <div><dt>Letzter Cloud-Abrufversuch</dt><dd>{state.fetchedAt ? stamp(state.fetchedAt, zone) : 'Noch keiner'}</dd></div>
            <div><dt>Abrufmodus</dt><dd>{state.timing.overviewVisible ? 'Übersicht sichtbar' : 'Hintergrund'} · mindestens {number(state.timing.cloudIntervalSeconds / 60)} Min.</dd></div>
            {state.timing.nextEligibleAt && <div><dt>Frühester nächster Abruf</dt><dd>{stamp(state.timing.nextEligibleAt, zone)}</dd></div>}
          </dl>
          <p>Die sichtbare Übersicht liest den lokalen Cache alle 30 Sekunden.</p>
          {state.quality === 'repeated' && <p>Sungrow hat beim letzten Abruf keinen neueren Messwert geliefert.</p>}
          {state.timing.retryAt && <p>Der letzte Abruf ist fehlgeschlagen. Die Wiederholung wartet bis {stamp(state.timing.retryAt, zone)}.</p>}
        </details>}
      </>}
    </Panel>
    <Panel title="Heute">
      <details onToggle={e => setShowToday(e.currentTarget.open)}>
      <summary>Heutigen Leistungsverlauf anzeigen</summary>
      {showToday && <TodayTimeline zone={zone} version={version} />}
      </details>
    </Panel>
  </>
}

export function TodayTimeline({ zone, version = 0, label = 'Heutiger Leistungsverlauf' }: {
  zone: string; version?: number; label?: string;
}) {
  const today = useData<Today>('/live/today', version)
  const timeline = today?.data
  const hour = new Intl.DateTimeFormat('de-DE', { timeZone: zone, hour: '2-digit', minute: '2-digit', timeZoneName: 'short' })
  function line(channel: TimelineChannel, source: string) {
    const samples = (timeline?.samples ?? []).filter(sample => sample.channel === channel && sample.source === source)
    const data: [number, number | null][] = []
    let previous: number | undefined
    for (const sample of samples) {
      const at = Date.parse(sample.at)
      const spanEnd = sample.end ? Date.parse(sample.end) : at
      const interrupted = previous !== undefined && (at - previous > (source === 'history' ? 0 : 15 * 60_000) ||
        source === 'live' && timeline?.gaps.some(gap => gap.channel === channel &&
          Date.parse(gap.start) < at && Date.parse(gap.end) > previous!))
      if (interrupted) data.push([(previous! + at) / 2, null])
      const value = sample.value * (channel === 'soc' ? 100 : 1)
      data.push([at, value])
      if (spanEnd > at) data.push([spanEnd - 1, value])
      previous = spanEnd
    }
    return data
  }
  return <>
      <p>Positive Netzleistung bedeutet Bezug, positive Batterieleistung Ladung.
        Momentwerte sind Stichproben. Der Tagesverlauf überbrückt keine Lücken.</p>
      {!timeline ? <Status error={today?.error} /> : <>
        {timeline.samples.length ? <Chart label={label} option={{
          animation: false, legend: { type: 'scroll' }, tooltip: { trigger: 'item', renderMode: 'richText',
            formatter: (point: { seriesName: string; value: [number, number] }) =>
              point.value[1] == null ? '' : point.seriesName + ' · ' + hour.format(point.value[0]) + ' · ' +
              (point.seriesName.startsWith('Ladezustand') ? number(point.value[1], 1) + ' %' : power(point.value[1])) },
          grid: { top: 52, bottom: 65, left: 52, right: 55 },
          xAxis: { type: 'time', min: Date.parse(timeline.start), max: Date.parse(timeline.end),
            axisLabel: { formatter: (at: number) => hour.format(at) } },
          yAxis: [{ type: 'value', name: 'Leistung', axisLabel: { formatter: power } }, { type: 'value', name: 'SOC %', min: 0, max: 100 }],
          dataZoom: [{ type: 'inside' }, { type: 'slider' }],
          series: timelineChannels.flatMap(([key, title], index) => ['live', 'history', 'synthetic'].map(source => ({
            name: title + (source === 'history' ? ' · Historie' : source === 'synthetic' ? ' · Demo' : ' · Stichprobe'),
            type: 'line', connectNulls: false, yAxisIndex: key === 'soc' ? 1 : 0,
            symbol: source === 'live' ? 'circle' : 'rect', symbolSize: 6, itemStyle: { color: colors[index] },
            data: line(key, source)
          })).filter(series => series.data.length))
        }} /> : <p>Für den lokalen Kalendertag liegen noch keine bestätigten Messwerte vor.</p>}
        {!!timeline.gaps.length && <details><summary>Erkannte Live-Lücken ({timeline.gaps.length})</summary>
          <ul>{timeline.gaps.map(gap => <li key={gap.channel + gap.start}>{timelineChannels.find(([key]) => key === gap.channel)?.[1]} ·
            {' '}{stamp(gap.start, zone)} bis {stamp(gap.end, zone)} · {gapReasons[gap.reason] ?? 'Abruf fehlgeschlagen'}</li>)}</ul></details>}
        {!!timeline.samples.length && <details><summary>Quellen und Messzeiten</summary><ul>
          {timelineChannels.flatMap(([key, title]) => {
            const samples = timeline.samples.filter(sample => sample.channel === key)
            const latest = samples.at(-1)
            return latest ? [<li key={key}>{title} · {samples.length} Werte · zuletzt {stamp(latest.at, zone)} · {
              latest.source === 'history' ? 'Historisches Intervall' : latest.source === 'synthetic' ? 'Demo' : 'Gespeicherte Live-Stichprobe'
            }</li>] : []
          })}
        </ul></details>}
      </>}
  </>
}
