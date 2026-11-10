import { useEffect, useState } from 'react'
import { request, useData } from './api'
import { Chart, colors, number, Panel, pct, stamp, Status } from './components'

const channels = [['pv', 'PV-Erzeugung (AC)'], ['load', 'Hausverbrauch'], ['grid', 'Netz'],
  ['battery', 'Batterie'], ['soc', 'Ladezustand']] as const
type Channel = typeof channels[number][0]
type Value = { value: number | null; direction: string; measuredAt: string | null; ageSeconds: number | null; quality: string }
type Now = Record<Channel, Value> & { pvSurplusKw: number | null; flowConfirmed: boolean; quality: string;
  pvDc?: Value | null; inverterActive?: Value | null;
  fetchedAt?: string | null; timing?: { overviewVisible: boolean; cloudIntervalSeconds: number; sourceMeasuredAt: string | null;
    nextEligibleAt: string | null; retryAt: string | null } }
type Today = { start: string; end: string; samples: { channel: Channel; at: string; value: number; source: string }[];
  gaps: { channel: Channel; start: string; end: string; reason: string }[] }
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

export function LiveView({ zone }: { zone: string }) {
  const [tab] = useState(tabId)
  const [version, refresh] = useState(0)
  const now = useData<Now>('/live/now', version)
  const [showToday, setShowToday] = useState(false)
  const today = useData<Today>(showToday ? '/live/today' : null, version)
  useEffect(() => {
    const signal = (visible: boolean) => { void request('/live/activity', 'POST', { tab, visible }).catch(() => {}) }
    const update = () => {
      const visible = document.visibilityState === 'visible'
      signal(visible)
      if (visible) refresh(v => v + 1)
    }
    signal(document.visibilityState === 'visible')
    document.addEventListener('visibilitychange', update)
    const timer = window.setInterval(() => { if (document.visibilityState === 'visible') update() }, 30_000)
    return () => {
      signal(false)
      document.removeEventListener('visibilitychange', update)
      window.clearInterval(timer)
    }
  }, [tab])
  const state = now?.data
  const timeline = today?.data
  function value(channel: Channel) {
    const reading = state?.[channel]
    if (reading?.value == null) return reading ? quality[reading.quality] ?? 'Kein Messwert' : 'Wird geladen …'
    if (channel === 'soc') return reading.value === 0 ? 'Leer · 0 %' : pct(reading.value)
    return number(Math.abs(reading.value), 3) + ' kW'
  }
  const hour = new Intl.DateTimeFormat('de-DE', { timeZone: zone, hour: '2-digit', minute: '2-digit', timeZoneName: 'short' })
  return <>
    <Panel title="Jetzt">
      {state?.quality === 'synthetic' && <p>Synthetische Live-Daten aus einem wiedergegebenen Demotag.</p>}
      {!state ? <Status error={now?.error} /> : <>
        {state.quality === 'unconfigured' && <p>Live-Messpunkte können in den Einstellungen geprüft und bestätigt werden.</p>}
        <div className="live-flow" aria-label="Aktuelle Energieflüsse">
          {channels.map(([key, title]) => key === 'pv' && state.pv.quality === 'unconfigured' && state.pvDc ? (
            <div className="live-pv" key={key}><strong>PV-DC-Leistung am Wechselrichter</strong>
              <span>{state.pvDc.value == null ? quality[state.pvDc.quality] ?? 'Kein Messwert' : number(state.pvDc.value, 3) + ' kW'}</span>
              <small>{state.pvDc.measuredAt && <>{stamp(state.pvDc.measuredAt, zone)} · </>}AC-PV-Fluss nicht separat gemessen</small>
            </div>
          ) : <div className={'live-' + key} key={key}>
            <strong>{title}</strong><span>{value(key)}</span>
            <small>{state.flowConfirmed && state[key].value !== 0 &&
              (key === 'pv' || key === 'grid' && state.grid.direction === 'import' ||
                key === 'battery' && state.battery.direction === 'discharge') ? '→ Anlage · ' :
              state.flowConfirmed && state[key].value !== 0 && (key === 'grid' || key === 'battery') ? '← Anlage · ' : ''}
              {key !== 'soc' && state[key].value !== null ? directions[state[key].direction] : null}
              {state[key].measuredAt && <> {stamp(state[key].measuredAt!, zone)}
                {state[key].ageSeconds != null && <> · {number(Math.floor(state[key].ageSeconds! / 60))} Min. alt</>}</>}</small>
          </div>)}
        </div>
        {(state.pvDc || state.inverterActive) && <dl className="facts" aria-label="Weitere Messwerte am Wechselrichter">
          {([['PV-DC-Leistung am Wechselrichter', state.pv.quality !== 'unconfigured' ? state.pvDc : null],
            ['Wechselrichter-Wirkleistung', state.inverterActive]] as const)
            .map(([label, reading]) => reading && <div key={label}><dt>{label}</dt>
              <dd>{reading.value == null ? quality[reading.quality] ?? 'Kein Messwert' : number(reading.value, 3) + ' kW'}
                {reading.measuredAt && <> · {stamp(reading.measuredAt, zone)}</>}</dd></div>)}
        </dl>}
        {state.pvSurplusKw != null && <p>Bestätigter PV-Überschuss {number(state.pvSurplusKw, 3)} kW.</p>}
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
      <p>Positive Netzleistung bedeutet Bezug, positive Batterieleistung Ladung.
        Momentwerte sind Stichproben. Der Tagesverlauf überbrückt keine Lücken.</p>
      {!timeline ? <Status error={today?.error} /> : <>
        {timeline.samples.length ? <Chart label="Heutiger Leistungsverlauf" option={{
          animation: false, legend: { type: 'scroll' }, tooltip: { trigger: 'item', renderMode: 'richText',
            formatter: (point: { seriesName: string; value: [number, number] }) =>
              point.seriesName + ' · ' + hour.format(point.value[0]) + ' · ' + number(point.value[1]) },
          grid: { top: 52, bottom: 65, left: 52, right: 55 },
          xAxis: { type: 'time', min: Date.parse(timeline.start), max: Date.parse(timeline.end),
            axisLabel: { formatter: (at: number) => hour.format(at) } },
          yAxis: [{ type: 'value', name: 'kW' }, { type: 'value', name: 'SOC %', min: 0, max: 100 }],
          dataZoom: [{ type: 'inside' }, { type: 'slider' }],
          series: channels.flatMap(([key, title], index) => ['live', 'history', 'synthetic'].map(source => ({
            name: title + (source === 'history' ? ' · Historie' : source === 'synthetic' ? ' · Demo' : ' · Stichprobe'), type: 'scatter', yAxisIndex: key === 'soc' ? 1 : 0,
            symbol: source === 'live' ? 'circle' : 'rect', symbolSize: 6, itemStyle: { color: colors[index] },
            data: timeline.samples.filter(sample => sample.channel === key && sample.source === source)
              .map(sample => [Date.parse(sample.at), sample.value * (key === 'soc' ? 100 : 1)])
          })).filter(series => series.data.length))
        }} /> : <p>Für den lokalen Kalendertag liegen noch keine bestätigten Messwerte vor.</p>}
        {!!timeline.gaps.length && <details><summary>Erkannte Live-Lücken ({timeline.gaps.length})</summary>
          <ul>{timeline.gaps.map(gap => <li key={gap.channel + gap.start}>{channels.find(([key]) => key === gap.channel)?.[1]} ·
            {' '}{stamp(gap.start, zone)} bis {stamp(gap.end, zone)} · {gapReasons[gap.reason] ?? 'Abruf fehlgeschlagen'}</li>)}</ul></details>}
      </>}
      </details>
    </Panel>
  </>
}
