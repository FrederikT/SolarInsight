import { useState } from 'react'
import { request, useData } from './api'
import { Numeric, Status } from './components'
import { LiveSetup } from './LiveView'

type AuthorizedPlant = { id: string; name: string }
const initialMapping = { pv: 0, load: 0, grid: 0, battery: 0, soc: 0, positiveGridIsImport: true,
  positiveBatteryIsCharge: true, instantaneous: true, endStamped: false, socPercent: true, confirmedAcPaths: false, stepMinutes: 5 }
const suggestedMinutes = { pv: 83067, load: 83106, grid: 83549, battery: 83238, soc: 83129 }
const emptyDaily = { pv: 0, load: 0, import: 0, export: 0, charge: 0, discharge: 0 }
const suggestedDaily = { pv: 83022, load: 83118, import: 83102, export: 83072, charge: 83322, discharge: 83323 }
const points = [['pv', 'PV-Leistung'], ['load', 'Hausverbrauchsleistung'], ['grid', 'Netzleistung'],
  ['battery', 'Batterieleistung'], ['soc', 'SOC']] as const
const dailyPoints = [['pv', 'PV'], ['load', 'Hausverbrauch'], ['import', 'Netzbezug'], ['export', 'Einspeisung'],
  ['charge', 'Ladung'], ['discharge', 'Entladung']] as const
type Point = typeof points[number][0]
type SavedMapping = Omit<typeof initialMapping, Point> & Record<Point, number | null> &
  { daily: Record<keyof typeof emptyDaily, number | null> | null }
type ProviderStatus = { configured: boolean; authorized: boolean;
  activeMapping: { id: string; name: string; zone: string; mapping: SavedMapping } | null }

export function ProviderSetup({ changed }: { changed: () => void }) {
  const [version, refresh] = useState(0)
  const state = useData<ProviderStatus>('/provider/status', version)
  const active = state?.data?.activeMapping
  const [plants, setPlants] = useState<AuthorizedPlant[]>([])
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState('')
  const [zone, setZone] = useState('Europe/Berlin')
  const [mapping, setMapping] = useState({ ...initialMapping, ...suggestedMinutes })
  const [daily, setDaily] = useState(suggestedDaily)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [probe, setProbe] = useState<{ minutes: { point: number; samples: number }[]; days: { point: number; samples: number }[] }>()
  async function action(run: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('')
    try { await run() }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }
  async function list(next: number) {
    await action(async () => {
      const rows = await request<AuthorizedPlant[]>('/provider/plants?page=' + next)
      setPlants(rows); select(rows.find(p => p.id === active?.id)?.id ?? rows[0]?.id ?? ''); setPage(next)
      if (!rows.length) setMessage('Keine weiteren autorisierten Anlagen.')
    })
  }
  function select(id: string) {
    setSelected(id)
    const saved = id === active?.id ? active : undefined
    setZone(saved?.zone ?? 'Europe/Berlin')
    if (!saved) {
      setMapping({ ...initialMapping, ...suggestedMinutes }); setDaily(suggestedDaily)
      return
    }
    const current = saved.mapping
    const hasDaily = dailyPoints.some(([key]) => current.daily?.[key] != null)
    // Replace the original PV-only setup that returned no rows in the historical probe.
    if (current.pv === 83067 && points.every(([key]) => key === 'pv' || current[key] == null) && !hasDaily) {
      setMapping({ ...initialMapping, load: 83106, stepMinutes: 15 })
      setDaily(suggestedDaily)
      return
    }
    setMapping({ ...current, pv: current.pv ?? 0, load: current.load ?? 0, grid: current.grid ?? 0,
      battery: current.battery ?? 0, soc: current.soc ?? 0 })
    setDaily({ ...emptyDaily, ...Object.fromEntries(dailyPoints.map(([key]) => [key, current.daily?.[key] ?? 0])) })
  }
  if (!state?.data) return <Status error={state?.error} />
  if (!state.data.configured) return <Status text="Die serverseitige App-Datei der freigegebenen Entwickleranwendung fehlt." />
  return <>
    {active && <div>
      <h3>Gespeicherte Messpunktzuordnung</h3>
      <p>{active.name} · {active.zone} · {active.mapping.stepMinutes} Minuten · {active.mapping.instantaneous ? 'Momentanwerte' : 'Intervallmittel'}</p>
      <ul>{points.map(([key, label]) => <li key={key}>{label} · {active.mapping[key] ?? 'Nicht zugeordnet'}</li>)}</ul>
      {active.mapping.pv != null && active.mapping.load == null &&
        <p>Ein PV-Testimport ist mit dieser Zuordnung möglich. Die gemeinsame Abdeckung bleibt ohne
          zugeordnete Hausverbrauchsreihe bei 0 %.</p>}
      {active.mapping.daily && dailyPoints.some(([key]) => active.mapping.daily?.[key] != null) &&
        <details><summary>Gespeicherte Tagesaggregate</summary><ul>
          {dailyPoints.map(([key, label]) => <li key={key}>{label} · {active.mapping.daily?.[key] ?? 'Nicht zugeordnet'}</li>)}
        </ul></details>}
    </div>}
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
    {active && state.data.authorized && <form onSubmit={e => {
      e.preventDefault()
      const day = new FormData(e.currentTarget).get('day')
      setProbe(undefined)
      void action(async () => setProbe(await request('/provider/probe', 'POST', { day })))
    }}>
      <h3>Historische Messpunkte prüfen</h3>
      <p>Die Prüfung fragt mehrere Messpunkte im 15-Minuten-Raster und als Tageswerte ab. Sie speichert keine Messwerte und ändert die Zuordnung nicht.</p>
      <label>Abgeschlossener Tag mit PV-Erzeugung<input type="date" name="day" required /></label>
      <button disabled={busy}>Messpunkte prüfen</button>
      {probe && <ul>{([['minutes', '15 Minuten'], ['days', 'Tageswerte']] as const).map(([key, label]) =>
        <li key={key}>{label} · {probe[key].map(p => `${p.point}: ${p.samples} Werte`).join(' · ')}</li>)}</ul>}
    </form>}
    {plants.length > 0 && <form onSubmit={e => {
      e.preventDefault()
      void action(async () => {
        await request('/provider/plant', 'PUT', { plant: selected, name: plants.find(plant => plant.id === selected)?.name, zone, mapping: { ...mapping, pv: mapping.pv || null, load: mapping.load || null, grid: mapping.grid || null,
          battery: mapping.battery || null, soc: mapping.soc || null,
          daily: Object.fromEntries(Object.entries(daily).map(([key, value]) => [key, value || null])) } })
        refresh(v => v + 1); changed(); setMessage('Anlage aktiviert. Speicherparameter und Tarife erfassen und den Import im Datenstatus starten.')
      })
    }}>
      <div className="fields"><label>Autorisierte Anlage<select value={selected} onChange={e => select(e.target.value)}>
        {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <label>Anlagenzeitzone<input value={zone} onChange={e => setZone(e.target.value)} required /></label></div>
      <h3>Bestätigte Messpunktzuordnung</h3>
      <p>Die IDs, den AC-Messort, die Einheiten und die Zeitsemantik für die gewählte Anlage anhand der Entwicklerdokumentation und einer autorisierten Probe prüfen.</p>
      <p>83106 ist ein Kandidat für Hausverbrauch im 15-Minuten-Raster.
        83033 bezeichnet Anlagenwirkleistung und benötigt vor einer Zuordnung als PV-Leistung einen Abgleich.</p>
      <button type="button" className="secondary" disabled={busy} onClick={() => {
        setMapping(current => ({ ...current, ...Object.fromEntries(points.filter(([key]) => !current[key])
          .map(([key]) => [key, suggestedMinutes[key]])) }))
        setDaily(current => ({ ...current, ...Object.fromEntries(dailyPoints.filter(([key]) => !current[key])
          .map(([key]) => [key, suggestedDaily[key]])) }))
      }}>Fehlende IDs aus dem Katalog vorbelegen</button>
      <p>Speicher-Tageswerte gelten für EMS-Anlagen. Die historische Probe zeigt, welche Kandidaten Werte liefern.</p>
      <button type="button" className="secondary" disabled={busy} onClick={() => {
        setMapping(current => ({ ...current, pv: 0, load: 0, grid: 0, battery: 0, soc: 0 }))
        setDaily(suggestedDaily)
      }}>Nur Tageswerte vorbelegen</button>
      <p>Die Tagesvorgabe setzt die fünf Minuten-IDs im Formular auf 0 und belegt PV, Hausverbrauch und Einspeisung.
        Gespeichert wird erst mit „Anlage und Zuordnung bestätigen“.</p>
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
      <details open><summary>Optionale Tagesaggregate (0 = unbekannt)</summary><div className="fields">
        {([['pv', 'PV'], ['load', 'Hausverbrauch'], ['import', 'Netzbezug'], ['export', 'Einspeisung'],
          ['charge', 'Ladung'], ['discharge', 'Entladung']] as const).map(([key, label]) => <Numeric key={key}
          label={'Tagesmesspunkt-ID · ' + label} value={daily[key]} step="1" change={value => setDaily({ ...daily, [key]: value })} />)}
      </div></details>
      <button disabled={busy}>Anlage und Zuordnung bestätigen</button>
    </form>}
    {active && <LiveSetup key={active.id} zone={active.zone} />}
    {busy && <Status text="Sungrow-Abfrage läuft. Das Abrufintervall der Anwendung wird berücksichtigt." />}
    {message && <Status text={message} />}
    {error && <Status error={error} />}
  </>
}

export function duration(seconds: number) {
  const minutes = Math.ceil(seconds / 60)
  if (minutes < 1) return 'unter einer Minute'
  const hours = Math.floor(minutes / 60)
  return hours ? `${hours} Std. ${minutes % 60} Min.` : `${minutes} Min.`
}

export function ImportForm({ changed }: { changed: () => void }) {
  const [test, setTest] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [estimate, setEstimate] = useState<{ windows: number; seconds: number }>()
  const [previewed, setPreviewed] = useState('')
  function reset() { setEstimate(undefined); setPreviewed('') }
  return <form onChange={reset} onSubmit={async e => {
    e.preventDefault(); setBusy(true); setError(''); setMessage('')
    const input = Object.fromEntries(new FormData(e.currentTarget))
    if (test) input.end = input.start
    const key = JSON.stringify(input)
    try {
      if (previewed !== key) {
        setEstimate(await request('/import-jobs/estimate', 'POST', input))
        setPreviewed(key)
      } else {
        await request('/import-jobs', 'POST', input)
        reset(); changed(); setMessage('Historischer Import eingeplant.')
      }
    }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }}><label>Importart<select value={test ? 'test' : 'days'} disabled={busy} onChange={e => setTest(e.target.value === 'test')}>
    <option value="days">Ganze Tage</option><option value="test">Kurzer Testimport</option>
  </select></label>
    {test && <p>Bis zu sechs Stunden an einem abgeschlossenen Tag in der Anlagenzeitzone.</p>}
    <div className="fields"><label>{test ? 'Testtag' : 'Importbeginn'}<input type="date" name="start" required /></label>
      {test ? <>
        <label>Beginn (Uhrzeit)<input type="time" name="startTime" defaultValue="11:00" required /></label>
        <label>Ende (Uhrzeit, exklusiv)<input type="time" name="endTime" defaultValue="12:00" required /></label>
      </> : <label>Importende exklusiv<input type="date" name="end" required /></label>}
    </div>
    {estimate && <p role="status">{estimate.windows} Zeitfenster · Basisdauer ca. {duration(estimate.seconds)}.
      Antwortzeiten, zusätzliche Abrufe und Wiederholungen können den Import verlängern.</p>}
    <button disabled={busy}>{estimate ? 'Import einplanen' : 'Dauer schätzen'}</button>
    {message && <Status text={message} />}{error && <Status error={error} />}
  </form>
}

