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

type LiveMapping = Record<Channel, number | null> & { deviceKey: string; positiveGridIsImport: boolean;
  positiveBatteryIsCharge: boolean; socPercent: boolean; confirmed: boolean; batteryExportExcluded: boolean; gridExport?: number | null; batteryDischarge?: number | null }
const initial: LiveMapping = { deviceKey: '', pv: null, load: null, grid: null, battery: null, soc: null,
  positiveGridIsImport: true, positiveBatteryIsCharge: true, socPercent: true, confirmed: false, batteryExportExcluded: false }
// Sungrow device type 14 uses the energy storage inverter measuring-point catalogue.
function defaultsForDevice(deviceKey: string): LiveMapping {
  return deviceKey.split('_')[1] === '14'
    ? { ...initial, deviceKey, load: 13119, grid: 13149, gridExport: 13121, battery: 13126,
        batteryDischarge: 13150, soc: 13141 }
    : { ...initial, deviceKey }
}

type Probe = { measuredAt: string | null; values: Partial<Record<Channel, number>>; missing: Channel[] }
export function LiveSetup({ zone, demo = false }: { zone: string; demo?: boolean }) {
  const [version, refresh] = useState(0)
  const data = useData<{ mapping: LiveMapping | null; devices: { key: string; model: string; type: string }[] }>('/live/mapping', version)
  const [draft, setDraft] = useState<LiveMapping>()
  const [probe, setProbe] = useState<Probe>()
  const [message, setMessage] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const devices = data?.data?.devices ?? []
  if (!devices.length) return data?.error ? <Status error={data.error} /> : null
  const mapping = draft ?? data?.data?.mapping ?? defaultsForDevice(devices.length === 1 ? devices[0].key : '')
  function change(next: LiveMapping) { setDraft(next); setProbe(undefined); setMessage('') }
  async function run(save: boolean) {
    setBusy(true); setError(undefined); setMessage('')
    try {
      if (save) { await request('/live/mapping', 'PUT', mapping); refresh(v => v + 1); setMessage('Live-Zuordnung gespeichert.') }
      else setProbe(await request('/live/probe', 'POST', mapping))
    } catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }
  return <form onSubmit={e => { e.preventDefault(); void run(true) }}>
    <h3>Live-Messpunkte prüfen</h3>
    <p>Die Probe liest ein Gerät. Messort, Vorzeichen, SOC-Skala, Zeitstempel und Aktualisierung anhand mehrerer
      Proben mit iSolarCloud vergleichen. Vorschläge für Hybridwechselrichter werden automatisch vorbelegt. Fehlende Kanäle in der Probe prüfen. Werte sind kW oder SOC-Anteile.</p>
    <label>Live-Gerät<select value={mapping.deviceKey} onChange={e => change(defaultsForDevice(e.target.value))} required>
      <option value="">Gerät wählen</option>{devices.map(device => <option key={device.key} value={device.key}>
        {device.type} · {device.model} · {device.key}</option>)}
    </select></label>
    {mapping.deviceKey.split('_')[1] === '14' && <p>Für PV ist in diesem Gerätekatalog nur die DC-Gesamtleistung dokumentiert.
      Für die Flussbilanz eine bestätigte PV-Leistung am passenden Messort zuordnen.</p>}
    <div className="fields">{channels.map(([key, label]) => <label key={key}>{key === 'grid' && mapping.gridExport != null
      ? 'Netzbezug' : key === 'battery' && mapping.batteryDischarge != null ? 'Batterieladung' : label} · Live-ID
      <input type="number" min={1} step={1} value={mapping[key] ?? ''} onChange={e =>
        change({ ...mapping, [key]: e.target.valueAsNumber || null, confirmed: false })} /></label>)}
      {([['gridExport', 'Netzeinspeisung'], ['batteryDischarge', 'Batterieentladung']] as const).map(([key, label]) =>
        <label key={key}>{label} · optionale Live-ID<input type="number" min={1} step={1} value={mapping[key] ?? ''}
          onChange={e => change({ ...mapping, [key]: e.target.valueAsNumber || null, confirmed: false })} /></label>)}
    </div>
    <p>Bei getrennten Richtungswerten muss die Probe beide IDs liefern. Ohne zweite ID gilt die gewählte Vorzeichenregel.</p>
    {([
      ['positiveGridIsImport', 'Positives Netzvorzeichen bedeutet Bezug'],
      ['positiveBatteryIsCharge', 'Positives Batterievorzeichen bedeutet Ladung'],
      ['socPercent', 'SOC kommt in Prozent'],
      ['batteryExportExcluded', 'Einspeisung aus der Batterie an der Anlage ausgeschlossen'],
      ['confirmed', 'Messpunkte, Messorte und Anlagenzeit anhand der Probe bestätigt'],
    ] as const).map(([key, label]) => <label className="check" key={key}><input type="checkbox" checked={mapping[key]}
      onChange={e => change({ ...mapping, ...(['positiveGridIsImport', 'positiveBatteryIsCharge', 'socPercent'].includes(key)
        ? { confirmed: false } : {}), [key]: e.target.checked })} />{label}</label>)}
    <div className="row"><button type="button" className="secondary" disabled={busy || !mapping.deviceKey} onClick={() => void run(false)}>
      Live-Probe abrufen</button><button disabled={demo || busy || !mapping.deviceKey}>Live-Zuordnung speichern</button></div>
    {busy && <Status text="Live-Abfrage läuft. Der Mindestabstand des Providers wird berücksichtigt." />}
    {probe && <p>{probe.measuredAt ? stamp(probe.measuredAt, zone) : 'Messzeit unbekannt'} · {channels.map(([key, label]) =>
      label + ' ' + (probe.values[key] == null ? 'unbekannt' : key === 'soc' ? pct(probe.values[key]) : number(probe.values[key]) + ' kW')).join(' · ')}</p>}
    {probe?.missing.length ? <Status error={'Keine verwertbare Messung für: ' +
      channels.filter(([key]) => probe.missing.includes(key)).map(([, label]) => label).join(', ') + '.'} /> : null}
    {message && <Status text={message} />}{error && <Status error={error} />}
  </form>
}
