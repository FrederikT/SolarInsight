import { useEffect, useState } from 'react'
import { request, useData } from './api'
import { Chart, colors, number, Panel, pct, stamp, Status } from './components'

const channels = [['pv', 'PV-Erzeugung'], ['load', 'Hausverbrauch'], ['grid', 'Netz'],
  ['battery', 'Batterie'], ['soc', 'Ladezustand']] as const
type Channel = typeof channels[number][0]
type Value = { value: number | null; direction: string; measuredAt: string | null; ageSeconds: number | null; quality: string }
type Now = Record<Channel, Value> & { pvSurplusKw: number | null; flowConfirmed: boolean; quality: string }
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
  const today = useData<Today>('/live/today', version)
  useEffect(() => {
    const signal = (visible: boolean) => { void request('/live/activity', 'POST', { tab, visible }).catch(() => {}) }
    const update = () => {
      const visible = document.visibilityState === 'visible'
      signal(visible)
      if (visible) refresh(v => v + 1)
    }
    update()
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
    if (reading?.value == null) return 'Unbekannt'
    return channel === 'soc' ? pct(reading.value) : number(Math.abs(reading.value)) + ' kW'
  }
  const hour = new Intl.DateTimeFormat('de-DE', { timeZone: zone, hour: '2-digit', minute: '2-digit', timeZoneName: 'short' })
  return <>
    <Panel title="Jetzt">
      {state?.quality === 'synthetic' && <p>Synthetische Live-Daten aus einem wiedergegebenen Demotag.</p>}
      {!state ? <Status error={now?.error} /> : <>
        {state.quality === 'unconfigured' && <p>Live-Messpunkte können in den Einstellungen geprüft und bestätigt werden.</p>}
        <p className="notice"><strong>{directions[state.grid.direction]} · {value('grid')}</strong>
          {state.grid.measuredAt && <> · {stamp(state.grid.measuredAt, zone)} · vor {number(state.grid.ageSeconds, 0)} s</>}</p>
        <div className="live-flow" aria-label="Aktuelle Energieflüsse">
          {channels.map(([key, title]) => <div className={'live-' + key} key={key}>
            <strong>{title}</strong><span>{value(key)}</span>
            <small>{state.flowConfirmed && state[key].value !== 0 &&
              (key === 'pv' || key === 'grid' && state.grid.direction === 'import' ||
                key === 'battery' && state.battery.direction === 'discharge') ? '→ Anlage · ' :
              state.flowConfirmed && state[key].value !== 0 && (key === 'grid' || key === 'battery') ? '← Anlage · ' : ''}
              {directions[state[key].direction] ?? ''} {state[key].value === null ? quality[state[key].quality] : ''}
              {state[key].value === null && state[key].ageSeconds != null ? ' · vor ' + number(state[key].ageSeconds, 0) + ' s' : ''}</small>
          </div>)}
        </div>
        <p>Bestätigter PV-Überschuss {state.pvSurplusKw == null ? 'unbekannt' : number(state.pvSurplusKw) + ' kW'}.</p>
      </>}
    </Panel>
    <Panel title="Heute">
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
    </Panel>
  </>
}
