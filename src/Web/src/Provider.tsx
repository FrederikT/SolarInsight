import { useState } from 'react'
import { request, useData } from './api'
import { Numeric, Status } from './components'

type AuthorizedPlant = { id: string; name: string }
const initialMapping = { pv: 0, load: 0, grid: 0, battery: 0, soc: 0, positiveGridIsImport: true,
  positiveBatteryIsCharge: true, instantaneous: true, endStamped: false, socPercent: true, confirmedAcPaths: false, stepMinutes: 5 }
export function ProviderSetup({ changed }: { changed: () => void }) {
  const [version, refresh] = useState(0)
  const state = useData<{ configured: boolean; authorized: boolean }>('/provider/status', version)
  const [plants, setPlants] = useState<AuthorizedPlant[]>([])
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState('')
  const [zone, setZone] = useState('Europe/Berlin')
  const [mapping, setMapping] = useState(initialMapping)
  const [daily, setDaily] = useState({ pv: 0, load: 0, import: 0, export: 0, charge: 0, discharge: 0 })
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  async function action(run: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('')
    try { await run() }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }
  async function list(next: number) {
    await action(async () => {
      const rows = await request<AuthorizedPlant[]>('/provider/plants?page=' + next)
      setPlants(rows); setSelected(rows[0]?.id || ''); setPage(next)
      if (!rows.length) setMessage('Keine weiteren autorisierten Anlagen.')
    })
  }
  if (!state?.data) return <Status error={state?.error} />
  if (!state.data.configured) return <Status text="Die serverseitige App-Datei der freigegebenen Entwickleranwendung fehlt." />
  return <>
    <p>Den Einwilligungslink aus dem Entwicklerportal öffnen und mit dem Anlageneigentümer freigeben.
      Den einmaligen Code aus der registrierten Rücksprungadresse hier eingeben.</p>
    <form onSubmit={e => {
      e.preventDefault()
      const form = e.currentTarget
      const code = String(new FormData(form).get('code') || '')
      void action(async () => {
        await request('/provider/authorize', 'POST', { code })
        form.reset(); refresh(v => v + 1); setMessage('App autorisiert. Anlagen können geladen werden.')
      })
    }}>
      <label>Einmaliger Autorisierungscode<input name="code" type="password" maxLength={256} required autoComplete="off" /></label>
      <button disabled={busy}>Code eintauschen</button>
    </form>
    {state.data.authorized && <div className="row actions">
      <button className="secondary" disabled={busy} onClick={() => list(1)}>Autorisierte Anlagen laden</button>
      {page > 1 && <button className="secondary" disabled={busy} onClick={() => list(page - 1)}>Vorherige Seite</button>}
      {plants.length === 10 && <button className="secondary" disabled={busy} onClick={() => list(page + 1)}>Nächste Seite</button>}
    </div>}
    {plants.length > 0 && <form onSubmit={e => {
      e.preventDefault()
      void action(async () => {
        await request('/provider/plant', 'PUT', { plant: selected, name: plants.find(plant => plant.id === selected)?.name, zone, mapping: { ...mapping, pv: mapping.pv || null, load: mapping.load || null, grid: mapping.grid || null,
          battery: mapping.battery || null, soc: mapping.soc || null,
          daily: Object.fromEntries(Object.entries(daily).map(([key, value]) => [key, value || null])) } })
        changed(); setMessage('Anlage aktiviert. Speicherparameter und Tarife erfassen und den Import im Datenstatus starten.')
      })
    }}>
      <div className="fields"><label>Autorisierte Anlage<select value={selected} onChange={e => setSelected(e.target.value)}>
        {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <label>Anlagenzeitzone<input value={zone} onChange={e => setZone(e.target.value)} required /></label></div>
      <h3>Bestätigte Messpunktzuordnung</h3>
      <p>Die IDs, den AC-Messort, die Einheiten und die Zeitsemantik für die gewählte Anlage anhand der Entwicklerdokumentation und einer autorisierten Probe prüfen.</p>
      <div className="fields">{([
        ['pv', 'PV-Leistung'], ['load', 'Hausverbrauchsleistung'], ['grid', 'Netzleistung'],
        ['battery', 'Batterieleistung (0 = unbekannt)'], ['soc', 'SOC (0 = unbekannt)'],
      ] as const).map(([key, label]) => <Numeric key={key} label={'Messpunkt-ID · ' + label} value={mapping[key]}
        step="1" change={value => setMapping({ ...mapping, [key]: value })} />)}</div>
      <label>Gemeinsame bestätigte Auflösung<select value={mapping.stepMinutes} onChange={e =>
        setMapping({ ...mapping, stepMinutes: Number(e.target.value) })}><option value={5}>5 Minuten</option>
        <option value={15}>15 Minuten</option></select></label>
      {([
        ['positiveGridIsImport', 'Positive Netzleistung bedeutet Bezug'],
        ['positiveBatteryIsCharge', 'Positive Batterieleistung bedeutet Ladung'],
        ['instantaneous', 'Momentanwerte (Trapeznäherung); sonst Intervallmittel'],
        ['endStamped', 'Intervallmittel sind am Intervallende gestempelt'],
        ['socPercent', 'SOC liegt in Prozent vor; sonst als Anteil von 0 bis 1'],
        ['confirmedAcPaths', 'AC-Messorte, Energiepfade und Betrieb ohne Netzladen oder Batterieexport bestätigt'],
      ] as const).map(([key, label]) => <label className="check" key={key}><input type="checkbox" checked={mapping[key]}
        onChange={e => setMapping({ ...mapping, [key]: e.target.checked })} />{label}</label>)}
      <details><summary>Optionale Tagesaggregate (0 = unbekannt)</summary><div className="fields">
        {([['pv', 'PV'], ['load', 'Hausverbrauch'], ['import', 'Netzbezug'], ['export', 'Einspeisung'],
          ['charge', 'Ladung'], ['discharge', 'Entladung']] as const).map(([key, label]) => <Numeric key={key}
          label={'Tagesmesspunkt-ID · ' + label} value={daily[key]} step="1" change={value => setDaily({ ...daily, [key]: value })} />)}
      </div></details>
      <button disabled={busy}>Anlage und Zuordnung bestätigen</button>
    </form>}
    {busy && <Status text="Sungrow-Abfrage läuft. Das Abrufintervall der Anwendung wird berücksichtigt." />}
    {message && <Status text={message} />}
    {error && <Status error={error} />}
  </>
}

export function ImportForm({ changed }: { changed: () => void }) {
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  return <form onSubmit={async e => {
    e.preventDefault(); setBusy(true); setError(''); setMessage('')
    const input = Object.fromEntries(new FormData(e.currentTarget))
    try { await request('/import-jobs', 'POST', input); changed(); setMessage('Historischer Import eingeplant.') }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }}><div className="fields"><label>Importbeginn<input type="date" name="start" required /></label>
    <label>Importende exklusiv<input type="date" name="end" required /></label></div>
    <button disabled={busy}>Historischen Import starten</button>
    {message && <Status text={message} />}{error && <Status error={error} />}
  </form>
}

