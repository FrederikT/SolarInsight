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
  deviceCounters?: boolean; live?: { deviceKey: string; confirmed: boolean } | null;
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
      <HistoricalProbe zone={active.zone} />
      {active.mapping.deviceCounters && active.mapping.live?.confirmed &&
        <CounterBackfill key={active.id} device={active.mapping.live.deviceKey} step={active.mapping.stepMinutes} />}
      <LiveDiagnostics key={active.id + JSON.stringify(active.mapping)} zone={active.zone} showCandidates />
    </details>}
  </>
}

type ProbeResult = {
  source: string; fetchedAt: string; failure?: string; httpStatus?: number; resultCode?: string; retryAfterSeconds?: number;
  points: { point: number; channel: string; unit: string | null; delivered: boolean; validCount: number; status: string;
    values: { measuredAt: string | null; value: number }[] }[];
}

const probeStatuses: Record<string, string> = {
  available: 'Geliefert', missing: 'Fehlt · Ursache ungeklärt', 'unknown-unit': 'Einheit fehlt oder ist unbekannt',
  'invalid-value': 'Kein numerischer Wert', 'invalid-time': 'Messzeit fehlt oder ist ungültig', 'not-evaluated': 'Nicht geprüft',
}

function probeInput(form: HTMLFormElement) {
  const data = new FormData(form)
  for (const key of ['start', 'end']) {
    const value = data.get(key)
    if (typeof value === 'string' && value.length === 5) data.set(key, value + ':00')
  }
  return Object.fromEntries(data)
}

type ProbeDevice = { key: string; model: string; type: string; candidateProbeSupported: boolean }

function CandidateProbe({ zone, devices }: { zone: string; devices: ProbeDevice[] }) {
  const [result, setResult] = useState<ProbeResult>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [historical, setHistorical] = useState(false)
  const [step, setStep] = useState(15)
  const supported = devices.filter(device => device.candidateProbeSupported)
  return <form onSubmit={async event => {
    event.preventDefault()
    const input = probeInput(event.currentTarget)
    setBusy(true); setError(''); setResult(undefined)
    try { setResult(await request(historical ? '/provider/probe/device-minutes' : '/provider/probe/live-candidates', 'POST',
      historical && step !== 15 ? { ...input, stepMinutes: step } : input)) }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }}>
    <h3>Gerätemesspunkte lesend prüfen</h3>
    <p>Die Rohwerte behalten Einheit und Vorzeichen der Quelle. Messort und SOC-Skalierung mit iSolarCloud abgleichen.</p>
    {!supported.length && <p>Für die gefundenen Geräte ist kein Kandidatenkatalog verfügbar.</p>}
    {!!supported.length && <>
      <label>Quelle<select value={historical ? 'history' : 'live'} disabled={busy}
        onChange={event => { setHistorical(event.target.value === 'history'); setResult(undefined); setError('') }}>
        <option value="live">Live-Kandidaten</option><option value="history">Gerätehistorie (15 Minuten)</option>
      </select></label>
      <label>Hybridwechselrichter<select name="device" required disabled={busy}
        onChange={() => { setResult(undefined); setError('') }}>
        {supported.map(device => <option key={device.key} value={device.key}>{device.type} · {device.model} · {device.key}</option>)}
      </select></label>
      {historical && <><label>Messraster<select value={step} disabled={busy}
        onChange={event => { setStep(Number(event.target.value)); setResult(undefined) }}>
        <option value={15}>15 Minuten</option><option value={5}>5 Minuten</option>
      </select></label><ProbeWindow step={step} /></>}
      <button className="secondary" disabled={busy}>Gerätemesspunkte abrufen</button>
    </>}
    {busy && <Status text="Geräteprobe läuft …" />}
    {error && <Status error={error} />}
    {result && <ProbeReport result={result} zone={zone} />}
  </form>
}

function ProbeReport({ result, zone }: { result: ProbeResult; zone: string }) {
  const failure = result.failure === 'authorization' ? 'Sungrow hat den Lesezugriff nicht autorisiert.' :
    result.failure === 'rate-limit' ? 'Abrufgrenze erreicht. Später erneut versuchen.' : result.failure
  return <>
    <p>{result.source} · {result.failure ? 'Prüfversuch' : 'Abgerufen'} {stamp(result.fetchedAt, zone)}</p>
    {failure && <Status error={[failure, result.httpStatus && 'HTTP ' + result.httpStatus,
      result.resultCode && 'Sungrow-Code ' + result.resultCode,
      result.retryAfterSeconds && 'Wartezeit ' + result.retryAfterSeconds + ' s'].filter(Boolean).join(' · ')} />}
    <ul>{result.points.map(point => <li key={point.point}>
      <strong>{point.channel} · {point.point}</strong> · {probeStatuses[point.status]} · {point.validCount} gültige Werte
      {point.delivered && ' · ' + (point.unit || 'Einheit unbekannt')}
      {!!point.values.length && <ul>{point.values.map((sample, index) => <li key={index}>
        {sample.measuredAt ? stamp(sample.measuredAt, zone) : 'Messzeit unbekannt'} · {sample.value} {point.unit}
      </li>)}</ul>}
    </li>)}</ul>
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

function ProbeWindow({ step = 15 }: { step?: number }) {
  return <>
    <label>Abgeschlossener Tag<input type="date" name="day" required /></label>
    <label>Von (Anlagenzeit)<input type="time" name="start" defaultValue="11:00" step={step * 60} required /></label>
    <label>Bis (Anlagenzeit, 00:00 für Tagesende)<input type="time" name="end" defaultValue="13:00" step={step * 60} required /></label>
  </>
}

function CounterBackfill({ device, step }: { device: string; step: number }) {
  type Result = { candidates?: unknown[]; written?: number; inconsistent?: string[]; failure?: string }
  const [preview, setPreview] = useState<{ input: ReturnType<typeof probeInput>; count: number }>()
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  async function run(input: ReturnType<typeof probeInput>, apply: boolean) {
    setBusy(true); setPreview(undefined); setError(''); setMessage('')
    try {
      const result = await request<Result>('/provider/device-counters/backfill' + (apply ? '?apply=true' : ''), 'POST',
        { ...input, device, stepMinutes: step })
      if (result.failure) throw new Error('Sungrow-Abruf fehlgeschlagen: ' + result.failure)
      if (result.inconsistent?.length && result.candidates?.length)
        throw new Error('Tagesabgleich für dieses Fenster fehlgeschlagen. Der Nachimport ist gesperrt.')
      const count = result.candidates?.length ?? 0
      if (apply) setMessage(`${result.written ?? 0} Intervalle neu gespeichert oder aktualisiert.`)
      else if (count) setPreview({ input, count })
      else setMessage('Sungrow hat für dieses Fenster keine importierbaren Zählerintervalle geliefert.')
    } catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }
  return <form onChange={() => setPreview(undefined)} onSubmit={event => {
    event.preventDefault()
    void run(probeInput(event.currentTarget), false)
  }}>
    <h3>Zählerdaten nachladen</h3>
    <p>Ein Fenster bis drei Stunden wählen. Solar Insight übernimmt geprüfte Zählerdifferenzen zwischen benachbarten Messpunkten. Fehlende Messintervalle bleiben sichtbar.</p>
    <fieldset disabled={busy}><ProbeWindow step={step} />
      <button className="secondary">Nachimport prüfen</button>
    </fieldset>
    {busy && <Status text="Sungrow-Daten werden geprüft …" />}
    {preview && <><p>{preview.count} Intervalle im gewählten Fenster sind importierbar.</p>
      <button type="button" onClick={() => void run(preview.input, true)}>Zählerintervalle übernehmen</button></>}
    {error && <Status error={error} />}
    {message && <Status text={message} />}
  </form>
}

function HistoricalProbe({ zone }: { zone: string }) {
  const [result, setResult] = useState<{ minuteReport?: ProbeResult; days: { point: number; samples: number }[];
    normalizedDays?: { point: number; kwh: number }[] }>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  return <form onSubmit={async e => {
    e.preventDefault()
    const input = probeInput(e.currentTarget)
    setBusy(true); setError(''); setResult(undefined)
    try { setResult(await request('/provider/probe', 'POST', input)) }
    catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }}>
    <h3>Historische Anlagenmesspunkte prüfen</h3>
    <ProbeWindow />
    <button className="secondary" disabled={busy}>Messpunkte prüfen</button>
    {busy && <Status text="Historische Probe läuft …" />}
    {error && <Status error={error} />}
    {result?.minuteReport && <ProbeReport result={result.minuteReport} zone={zone} />}
    {result && <ul>{result.days.map(point => {
      const value = result.normalizedDays?.find(row => row.point === point.point)?.kwh
      return <li key={point.point}>Tagespunkt {point.point} · {value == null ? point.samples + ' Rohwerte' : number(value) + ' kWh'}</li>
    })}</ul>}
  </form>
}

export function LiveDiagnostics({ zone, showCandidates = false }: { zone: string; showCandidates?: boolean }) {
  const data = useData<{ mapping: (PointIds & PointSemantics & { deviceKey: string; confirmed: boolean;
    gridExport?: number | null; batteryDischarge?: number | null; batteryExportExcluded: boolean;
    pvDc?: number | null; inverterActive?: number | null }) | null;
    devices: ProbeDevice[] }>('/live/mapping')
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
      <PointList rows={[['PV-DC-Leistung am Wechselrichter', mapping.pvDc],
        ['Wechselrichter-Wirkleistung', mapping.inverterActive]]} missing="Kein bestätigter Gerätemesspunkt." />
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
    {showCandidates && <CandidateProbe zone={zone} devices={devices} />}
  </>
}
