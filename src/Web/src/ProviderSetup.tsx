import { useState } from 'react'
import { request, useData } from './api'
import { number, pct, stamp, Status } from './components'

type AuthorizedPlant = { id: string; name: string }
const points = [['pv', 'PV-Erzeugung'], ['load', 'Hausverbrauch'], ['grid', 'Netzleistung'],
  ['battery', 'Batterieleistung'], ['soc', 'Ladezustand']] as const
const dailyPoints = [['pv', 'PV-Erzeugung'], ['load', 'Hausverbrauch'], ['import', 'Netzbezug'],
  ['export', 'Einspeisung'], ['charge', 'Batterieladung'], ['discharge', 'Batterieentladung']] as const
type Channel = typeof points[number][0]
type PointIds = Record<Channel, number | null>
type PointSemantics = { positiveGridIsImport: boolean; positiveBatteryIsCharge: boolean; socPercent: boolean }
type Mapping = PointIds & PointSemantics & { stepMinutes: number; instantaneous: boolean; endStamped: boolean; confirmedAcPaths: boolean;
  daily: Record<typeof dailyPoints[number][0], number | null> | null }
export type ProviderStatus = { configured: boolean; authorized: boolean; defaultZone: string;
  activeMapping: { id: string; name: string; zone: string; mapping: Mapping } | null }

export function connectionLabel(state: ProviderStatus) {
  if (!state.configured) return 'Entwickleranwendung fehlt'
  if (!state.authorized) return 'Autorisierung erforderlich'
  return state.activeMapping ? 'Anlage verbunden' : 'Anlagenwahl ausstehend'
}

export function ProviderSetup({ changed }: { changed: () => void }) {
  const [version, refresh] = useState(0)
  const state = useData<ProviderStatus>('/provider/status', version)
  const [plants, setPlants] = useState<AuthorizedPlant[]>()
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState('')
  const [zone, setZone] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  if (!state?.data) return <Status error={state?.error} />
  const data = state.data
  const active = data.activeMapping
  if (!data.configured) return <>
    <Status text="Hinterlege die App-Datei einer für die EU freigegebenen Sungrow-Entwickleranwendung auf dem Server und starte Solar Insight neu." />
    <a href="https://academy.sungrow-emea.com/de/articles/331036-sungrow-s-api-developer-portal" rel="noreferrer">Sungrow-Anleitung zur Entwicklerfreigabe</a>
  </>
  async function action(run: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('')
    try { await run() }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }
  function select(id: string) {
    setSelected(id)
    setZone(id === active?.id ? active.zone : data.defaultZone)
  }
  async function load(next: number) {
    const rows = await request<AuthorizedPlant[]>('/provider/plants?page=' + next)
    setPlants(rows)
    select(rows.find(p => p.id === active?.id)?.id ?? rows[0]?.id ?? '')
    setPage(next)
  }
  const authorize = <form onSubmit={e => {
    e.preventDefault()
    const form = e.currentTarget
    const code = String(new FormData(form).get('code') || '')
    void action(async () => {
      await request('/provider/authorize', 'POST', { code })
      form.reset(); refresh(v => v + 1); changed()
      await load(1)
    })
  }}>
    <p>Öffne den Einwilligungslink der Entwickleranwendung mit dem Sungrow-Eigentümerkonto und übernimm den einmaligen Code aus der Rücksprungadresse.</p>
    <label>Autorisierungscode<input name="code" type="password" maxLength={256} required autoComplete="off" /></label>
    <button disabled={busy}>Autorisieren</button>
  </form>
  const selection = <>
    <button className="secondary" disabled={busy} onClick={() => action(() => load(1))}>Autorisierte Anlagen laden</button>
    {plants && <div className="row actions">
      {page > 1 && <button className="secondary" disabled={busy} onClick={() => action(() => load(page - 1))}>Vorherige Seite</button>}
      {plants.length === 10 && <button className="secondary" disabled={busy} onClick={() => action(() => load(page + 1))}>Nächste Seite</button>}
    </div>}
    {plants?.length === 0 && <Status text="Auf dieser Seite sind keine Anlagen verfügbar. Prüfe bei der Autorisierung, welche Anlagen freigegeben wurden." />}
    {!!plants?.length && <form onSubmit={e => {
      e.preventDefault()
      void action(async () => {
        await request('/provider/plant', 'PUT', { plant: selected, zone, page })
        setPlants(undefined); refresh(v => v + 1); changed()
        setMessage('Anlage aktiviert. Den Importfortschritt findest du im Datenstatus.')
      })
    }}>
      <div className="fields"><label>Autorisierte Anlage<select value={selected} onChange={e => select(e.target.value)}>
        {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <label>Anlagenzeitzone<input value={zone} onChange={e => setZone(e.target.value)} required /></label></div>
      <button disabled={busy}>Anlage aktivieren</button>
    </form>}
  </>
  return <>
    <p><span className="badge">{connectionLabel(data)}</span></p>
    {active && <p>{active.name} · {active.zone}</p>}
    {active && !points.some(([key]) => active.mapping[key] != null) &&
      <p>Für Minutenanalysen fehlt ein bestätigtes Betreiberprofil. Zugeordnete Tageswerte bleiben verfügbar.</p>}
    {data.authorized ? <>
      {active && !plants ? <details><summary>Anlage wechseln oder Betreiberprofil anwenden</summary>{selection}</details> : selection}
      <details><summary>Sungrow erneut autorisieren</summary>{authorize}</details>
    </> : authorize}
    {busy && <Status text="Sungrow-Abfrage läuft. Das Abrufintervall wird berücksichtigt." />}
    {message && <Status text={message} />}
    {error && <Status error={error} />}
    {active && <details><summary>Diagnose und Messpunkte</summary>
      <h3>Historische Zuordnung</h3>
      {points.some(([key]) => active.mapping[key] != null) && <>
        <p>{active.mapping.stepMinutes} Minuten · {active.mapping.instantaneous ? 'Momentanwerte' :
          'Intervallmittel am Intervall' + (active.mapping.endStamped ? 'ende' : 'beginn')} ·
          {' '}{active.mapping.confirmedAcPaths ? 'AC-Pfade bestätigt' : 'AC-Pfade nicht bestätigt'}</p>
        <Semantics mapping={active.mapping} />
      </>}
      <PointList rows={points.map(([key, label]) => [label, active.mapping[key]])} missing="Kein bestätigter Minutenmesspunkt für diese Anlage." />
      <h3>Tageswerte</h3>
      <PointList rows={dailyPoints.map(([key, label]) => [label, active.mapping.daily?.[key]])} missing="Kein Tagesmesspunkt im Anlagenprofil." />
      <HistoricalProbe />
      <LiveDiagnostics key={active.id + JSON.stringify(active.mapping)} zone={active.zone} />
    </details>}
  </>
}

function PointList({ rows, missing }: { rows: [string, number | null | undefined][]; missing: string }) {
  return <ul>{rows.map(([label, point]) => <li key={label}>{label} · {point ?? missing}</li>)}</ul>
}

function Semantics({ mapping }: { mapping: PointIds & PointSemantics & { gridExport?: number | null; batteryDischarge?: number | null } }) {
  const rules = [
    mapping.grid == null ? null : mapping.gridExport != null ? 'Einspeisepunkt ' + mapping.gridExport :
      'Positiver Netzwert bedeutet ' + (mapping.positiveGridIsImport ? 'Bezug' : 'Einspeisung'),
    mapping.battery == null ? null : mapping.batteryDischarge != null ? 'Entladepunkt ' + mapping.batteryDischarge :
      'Positiver Batteriewert bedeutet ' + (mapping.positiveBatteryIsCharge ? 'Ladung' : 'Entladung'),
    mapping.soc == null ? null : 'Ladezustand ' + (mapping.socPercent ? 'in Prozent' : 'als Anteil von 0 bis 1'),
  ].filter(Boolean)
  return rules.length ? <p>{rules.join(' · ')}</p> : null
}

function HistoricalProbe() {
  const [result, setResult] = useState<{ minutes: { point: number; samples: number }[]; days: { point: number; samples: number }[] }>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  return <form onSubmit={async e => {
    e.preventDefault()
    const day = new FormData(e.currentTarget).get('day')
    setBusy(true); setError(''); setResult(undefined)
    try { setResult(await request('/provider/probe', 'POST', { day })) }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }}>
    <h3>Historische Messpunkte prüfen</h3>
    <label>Abgeschlossener Tag mit PV-Erzeugung<input type="date" name="day" required /></label>
    <button className="secondary" disabled={busy}>Messpunkte prüfen</button>
    {busy && <Status text="Historische Probe läuft …" />}
    {error && <Status error={error} />}
    {result && <ul>{([['minutes', '15 Minuten'], ['days', 'Tageswerte']] as const).map(([key, label]) =>
      <li key={key}>{label} · {result[key].map(p => p.point + ': ' + p.samples + ' Werte').join(' · ')}</li>)}</ul>}
  </form>
}

export function LiveDiagnostics({ zone }: { zone: string }) {
  const data = useData<{ mapping: (PointIds & PointSemantics & { deviceKey: string; confirmed: boolean;
    gridExport?: number | null; batteryDischarge?: number | null; batteryExportExcluded: boolean }) | null;
    devices: { key: string; model: string; type: string }[] }>('/live/mapping')
  const [probe, setProbe] = useState<{ measuredAt: string | null; values: Partial<Record<Channel, number>>; missing: Channel[] }>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  if (!data?.data) return <Status error={data?.error} />
  const { mapping, devices } = data.data
  const device = devices.find(d => d.key === mapping?.deviceKey)
  return <>
    <h3>Live-Messpunkte</h3>
    {device && <p>{device.type} · {device.model}</p>}
    <PointList rows={points.map(([key, label]) => [label, mapping?.confirmed ? mapping[key] : null])}
      missing="Kein bestätigter Live-Messpunkt für diese Anlage." />
    {mapping?.confirmed && <>
      <Semantics mapping={mapping} />
      <p>{mapping.batteryExportExcluded ? 'Batterieexport ausgeschlossen' : 'Batterieexport kann auftreten'}</p>
    </>}
    {mapping?.confirmed ? <button className="secondary" disabled={busy} onClick={async () => {
      setBusy(true); setError(''); setProbe(undefined)
      try { setProbe(await request('/live/probe', 'POST')) }
      catch (err) { setError((err as Error).message) }
      finally { setBusy(false) }
    }}>Live-Probe abrufen</button> : <p>Für Live-Daten muss der Betreiber ein bestätigtes Profil auf dem Server hinterlegen.</p>}
    {busy && <Status text="Live-Probe läuft …" />}
    {probe && <p>{probe.measuredAt ? stamp(probe.measuredAt, zone) : 'Messzeit unbekannt'} · {points.map(([key, label]) => {
      const value = probe.values[key]
      return label + ' ' + (value == null ? 'unbekannt' : key === 'soc' ? pct(value) : number(value) + ' kW')
    }).join(' · ')}</p>}
    {!!probe?.missing.length && <Status text={'Keine verwertbare Messung für ' +
      points.filter(([key]) => probe.missing.includes(key)).map(([, label]) => label).join(', ') + '.'} />}
    {error && <Status error={error} />}
  </>
}
