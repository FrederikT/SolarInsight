import { lazy, Suspense, useCallback, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { request, session, useData, type EnergyEvent, type Period, type Plant, type Report } from './api'
import { bounds, date, eventBounds, localDay, Numeric, number, Panel, pct, stamp, Status } from './components'
import { Overview } from './Overview'
import { PeriodFields } from './PeriodFields'
import { SettingsView } from './Settings'
import { duration, ImportForm } from './Provider'
import { LiveView } from './LiveView'
import './style.css'

const ScenarioEditor = lazy(() => import('./Scenario').then(m => ({ default: m.ScenarioEditor })))

const pages = [['overview', 'Übersicht'], ['potential', 'Speicher & Potenzial'], ['status', 'Datenstatus'], ['settings', 'Einstellungen']]
const kinds = [['day', 'Tag'], ['week', 'Woche'], ['month', 'Monat'], ['quarter', 'Quartal'],
  ['season', 'Jahreszeit'], ['year', 'Jahr'], ['all', 'Seit Datenbeginn'], ['custom', 'Frei']]
const iso = (d: Date) => d.toISOString().slice(0, 10)
const plusDay = (s: string, count: number) => iso(new Date(Date.parse(s) + count * 86400000))
type Session = Awaited<ReturnType<typeof session>>

function App() {
  const [auth, setAuth] = useState<Session>()
  const [error, setError] = useState<string>()
  useEffect(() => { session().then(setAuth).catch(e => setError(e.message)) }, [])
  if (!auth) return <main><h1>Solar Insight</h1><Status error={error} /></main>
  if (auth.demo || auth.authenticated) return <Dashboard demo={auth.demo} refreshSession={async () => setAuth(await session())} />
  return <main className="login"><Panel title="Solar Insight">
    <p>Mit dem lokalen Anlagenzugang anmelden.</p>
    <form onSubmit={async e => {
      e.preventDefault(); setError(undefined)
      const data = new FormData(e.currentTarget)
      try { await request('/auth/login', 'POST', { password: data.get('password') }); setAuth(await session()) }
      catch (err) { setError((err as Error).message) }
    }}><label>Passwort<input name="password" type="password" required autoComplete="current-password" /></label>
      {error && <Status error={error} />}<button>Anmelden</button></form>
  </Panel></main>
}

function Dashboard({ demo, refreshSession }: { demo: boolean; refreshSession: () => Promise<void> }) {
  const [url, setUrl] = useState(() => new URLSearchParams(location.search))
  const [version, refresh] = useState(0)
  const [cancelError, setCancelError] = useState<string>()
  const [cancelling, setCancelling] = useState(false)
  const [simulation, setSimulation] = useState<string | null>(null)
  const plant = useData<Plant>('/plant', version)
  const update = useCallback((patch: Record<string, string | null>) => {
    const next = new URLSearchParams(location.search)
    Object.entries(patch).forEach(([key, value]) => value == null ? next.delete(key) : next.set(key, value))
    history.pushState({}, '', '?' + next.toString())
    setUrl(next)
  }, [])
  useEffect(() => {
    const pop = () => setUrl(new URLSearchParams(location.search))
    window.addEventListener('popstate', pop)
    return () => window.removeEventListener('popstate', pop)
  }, [])
  const drill = useCallback((day: string, exclusive?: string | null) => update({ page: 'overview', detail: day, detailEnd: exclusive ?? null }), [update])
  const page = url.get('page') || 'overview'
  const detail = page === 'overview' ? url.get('detail') : null
  const detailEnd = detail ? url.get('detailEnd') : null
  const day = detailEnd ? null : detail
  const kind = url.get('kind') || 'month'
  const anchor = url.get('anchor') || plant?.data?.defaultAnchor || ''
  const start = url.get('start') || (anchor ? anchor.slice(0, 7) + '-01' : '')
  const end = url.get('end') || (anchor ? plusDay(anchor, 1) : '')
  const query = new URLSearchParams(detail && detailEnd ? { kind: 'custom', start: detail, end: detailEnd } :
    { kind: day ? 'day' : kind, ...(day || anchor ? { anchor: day || anchor } : {}),
      ...(kind === 'custom' && !day ? { start, end } : {}) }).toString()
  const status = useData<{ report: Report; latest: string | null; provider: string; progress: null | { percent: number; remainingWindows: number; seconds: number | null }; job: null | {
    id: string; windows: number; emptyWindows: number; readingRows: number; aggregateRows: number; cancelled: string | null; status: string; checkpoint: string; start: string; end: string; error: string | null; lastAttempt: string | null; lastSuccess: string | null };
    repair: { start: string; end: string; kind: string; attempts: number; due: string; open: boolean }[];
    gaps: { start: string; end: string }[] }>('/data-status?' + query, version)
  const runningImport = status?.data?.job?.status === 'pending' || status?.data?.job?.status === 'running'
  useEffect(() => {
    if (!runningImport) return
    const timer = window.setInterval(() => refresh(v => v + 1), 5000)
    return () => window.clearInterval(timer)
  }, [runningImport])
  const shift = (direction: number) => {
    const d = new Date((day || anchor) + 'T12:00:00Z')
    if (day || kind === 'day' || kind === 'week') d.setUTCDate(d.getUTCDate() + (day || kind === 'day' ? 1 : 7) * direction)
    else { d.setUTCDate(1); d.setUTCMonth(d.getUTCMonth() + direction * (kind === 'year' ? 12 : kind === 'month' ? 1 : 3)) }
    update(day ? { detail: iso(d) } : { anchor: iso(d) })
  }
  const zone = plant?.data?.zone || 'Europe/Berlin'
  const logout = async () => { await request('/auth/logout', 'POST'); await refreshSession() }
  return <>
    <header><div className="header-inner"><a className="brand" href="/" aria-label="Solar Insight Startseite"><span aria-hidden>☀</span> Solar Insight</a>
      <span className="plant">{plant?.data?.name ?? 'PV-Analyse'}</span>{demo && <span className="badge">Synthetische Demodaten</span>}
      {!demo && <button className="secondary" onClick={logout}>Abmelden</button>}</div></header>
    <nav aria-label="Hauptnavigation"><div>{pages.map(([id, title]) => <button key={id} aria-current={page === id ? 'page' : undefined}
      onClick={() => update({ page: id, detail: null, detailEnd: null })}>{title}</button>)}</div></nav>
    <main>
      {!plant?.data ? <Status error={plant?.error} /> : <>
        {page !== 'settings' && <div className="period row">
          {detail ? <button className="secondary" onClick={() => update({ detail: null, detailEnd: null })}>Zurück zum Zeitraum</button> :
            <label>Zeitraum<select aria-label="Zeitraum" value={kind} onChange={e => update({ kind: e.target.value })}>{kinds.map(([id, label]) =>
              <option key={id} value={id}>{label}</option>)}</select></label>}
          {!detailEnd && (kind !== 'all' && kind !== 'custom' || day) ? <>
            <button className="secondary" aria-label="Vorheriger Zeitraum" onClick={() => shift(-1)}>←</button>
            {!day && ['week', 'month', 'quarter', 'season'].includes(kind) ? <PeriodFields kind={kind} anchor={anchor}
              change={value => update({ anchor: value })} /> : <label>Stichtag<input type="date" value={day || anchor} onChange={e => e.target.value && update(day ?
                { detail: e.target.value } : { anchor: e.target.value })} /></label>}
            <button className="secondary" aria-label="Nächster Zeitraum" onClick={() => shift(1)}>→</button>
          </> : !detailEnd && kind === 'custom' && <><label>Beginn<input type="date" value={start} onChange={e => e.target.value && update({ start: e.target.value })} /></label>
            <label>Ende exklusiv<input type="date" value={end} onChange={e => e.target.value && update({ end: e.target.value })} /></label></>}
          {status?.data && <span>{bounds(status.data.report.period)}</span>}
        </div>}
        {runningImport && status.data?.progress && <Status text={`Datenimport · ${status.data.progress.percent} % verarbeitet`} />}
        {page === 'overview' && <LiveView zone={zone} />}
        {page === 'overview' && <Overview query={query} drill={drill} day={day} evidence={!!detailEnd} simulation={simulation} version={version} />}
        {page === 'potential' && <><h1>Speicher & Potenzial</h1>
          {status?.data?.report && plant.data.capabilities.simulation ? <Suspense fallback={<Status />}><ScenarioEditor period={status.data.report.period}
            simulation={simulation} setSimulation={setSimulation} drill={drill} /></Suspense> : <Status error={status?.error}
              text={plant.data.capabilities.simulation ? 'Zeitraum wird geladen …' : 'Simulation benötigt PV- und Verbrauchszeitreihen und bestätigte Speicherparameter.'} />}
          <Surplus query={query} zone={zone} drill={drill} /></>}
        {page === 'status' && <><h1>Datenstatus</h1>{status?.data ? <>
          <Panel title="Import und Verbindung"><p>{status.data.provider}</p>
            {status.data.job && <dl className="facts">{Object.entries({
              Importstatus: ({ pending: 'Ausstehend', running: 'Läuft', completed: 'Abgeschlossen', failed: 'Fehlgeschlagen', cancelled: 'Abgebrochen' } as Record<string, string>)[status.data.job.status],
              'Abgebrochen am': status.data.job.cancelled && stamp(status.data.job.cancelled, zone),
              Importbeginn: stamp(status.data.job.start, zone), Importende: stamp(status.data.job.end, zone),
              'Verarbeitet bis': stamp(status.data.job.checkpoint, zone), 'Letzter Versuch': status.data.job.lastAttempt && stamp(status.data.job.lastAttempt, zone),
              'Zuletzt Messwerte verarbeitet': status.data.job.error !== 'no-data' && status.data.job.lastSuccess
                ? stamp(status.data.job.lastSuccess, zone) : null, Fehlerkategorie: status.data.job.error,
            }).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v || '—'}</dd></div>)}</dl>}
            {status.data.job && status.data.progress && <div>
              <label>Importfortschritt · {status.data.progress.percent} %
                <progress max={100} value={status.data.progress.percent} /></label>
              <p>{status.data.job.windows} verarbeitete Zeitfenster, davon {status.data.job.emptyWindows} leer ·
                {status.data.job.readingRows} gespeicherte Messintervalle · {status.data.job.aggregateRows} Tageswerte</p>
              {runningImport && <p>Noch ca. {status.data.progress.remainingWindows} Zeitfenster · {status.data.progress.seconds === null
                ? 'Restzeit wird ermittelt' : 'ca. ' + duration(status.data.progress.seconds)}</p>}
            </div>}
            {!demo && runningImport && <button className="secondary" disabled={cancelling} onClick={async () => {
              setCancelling(true); setCancelError(undefined)
              try { await request('/import-jobs/' + status.data!.job!.id, 'DELETE') }
              catch (err) { setCancelError((err as Error).message) }
              finally { setCancelling(false); refresh(v => v + 1) }
            }}>Import abbrechen</button>}
            {cancelError && <Status error={cancelError} />}
            {status.data.job?.error === 'no-data' && <Status error="Im geprüften Bereich waren keine verwertbaren Messwerte verfügbar. Wähle in iSolarCloud einen abgeschlossenen Tag mit sichtbarer PV-Leistung und prüfe denselben Zeitraum per kurzem Testimport. Bleibt der Test leer, prüfe die Diagnose im App-Protokoll." />}
            <p>Jüngster Messzeitpunkt {status.data.latest ? stamp(status.data.latest, zone) : '—'} · Revision {status.data.report.revision}</p>
            {!demo && plant.data.capabilities.energy && !runningImport && <ImportForm changed={() => refresh(v => v + 1)} />}</Panel>
          {!!status.data.repair.length && <Panel title="Historische Abstimmung">
            <ul>{status.data.repair.map(gap => <li key={gap.kind + gap.start}>{stamp(gap.start, zone)} bis {stamp(gap.end, zone)} ·
              {gap.kind === 'daily' ? ' Tageswerte' : ' Minutenwerte'} · {gap.open ? 'offen nach ' + gap.attempts + ' Versuchen' :
                'eingeplant ab ' + stamp(gap.due, zone)}</li>)}</ul>
          </Panel>}
          <Panel title="Abdeckung und Datenlücken"><p>Gemeinsame Abdeckung von PV und Hausverbrauch {pct(status.data.report.metrics.coverage)} · Auflösung {number(status.data.report.resolutionMinutes)} Minuten</p>
            {status.data.gaps.length ? <ul>{status.data.gaps.map(g => <li key={g.start}>{stamp(g.start, zone)} bis {stamp(g.end, zone)}</li>)}</ul> :
              <p>Keine Lücken in der gemeinsamen PV- und Verbrauchsreihe.</p>}</Panel></> : <Status error={status?.error} />}</>}
        {page === 'settings' && <SettingsView demo={demo} plant={plant.data} onChange={() => refresh(v => v + 1)} logout={refreshSession} />}
      </>}
    </main><footer>Solar Insight · Lokale PV-Analyse</footer>
  </>
}

function Surplus({ query, zone, drill }: { query: string; zone: string; drill: (day: string, exclusive?: string | null) => void }) {
  const [kw, setKw] = useState(1)
  const [threshold, setThreshold] = useState(.1)
  const [from, setFrom] = useState(9)
  const [to, setTo] = useState(17)
  const [offset, setOffset] = useState(0)
  const [type, setType] = useState('')
  const result = useData<{ period: Period; export: number | null; captured: number | null; coverage: number; assumption: string }>(
    '/surplus?' + query + '&kw=' + kw + '&from=' + Math.round(from * 60) + '&to=' + Math.round(to * 60))
  const events = useData<{ events: EnergyEvent[]; total: number }>('/events?' + query + '&offset=' + offset + '&limit=10&threshold=' +
    threshold + (type ? '&type=' + encodeURIComponent(type) : ''))
  return <>
    <Panel title="Theoretisch auffangbarer Export">
      <div className="fields"><Numeric label="Aufnahmegrenze (kW)" value={kw} change={setKw} max={100} />
        <Numeric label="Lokale Stunde ab" value={from} change={setFrom} max={23} /><Numeric label="Lokale Stunde bis" value={to} change={setTo} max={24} /></div>
      {result?.data ? <><p>Erfasster Export {number(result.data.export)} kWh · Auffangbar {number(result.data.captured)} kWh · Abdeckung {pct(result.data.coverage)}</p>
        <p>{result.data.assumption}</p></> : <Status error={result?.error} />}
    </Panel>
    <Panel title="Speicher- und Überschussereignisse">
      <div className="fields"><Numeric label="Ereignisschwelle (kW)" value={threshold} change={v => { setOffset(0); setThreshold(v) }} min={.001} max={100} />
        <label>Ereignistyp<select value={type} onChange={e => { setOffset(0); setType(e.target.value) }}>
          {['', 'Überschussepisode', 'Einspeisung an oberer Ladegrenze', 'Netzbezug an unterer Ladegrenze',
            'Einspeisung bei freiem Speicher', 'Hohe Last bei geringer PV-Erzeugung'].map(t => <option key={t} value={t}>{t || 'Alle'}</option>)}</select></label></div>
      {events?.data ? <><p>{events.data.total} Ereignisse. Einspeisung bei freiem Speicher ist eine Beobachtung ohne Ursachenzuordnung.</p>
        <ul>{events.data.events.map(e => <li key={e.type + e.start}><button className="link" onClick={() => { const p = eventBounds(e.start, e.end, zone); drill(p.first, p.exclusive) }}>
          {date(localDay(e.start, zone))}</button> · {e.text} Spitze {number(e.peakKw)} kW.</li>)}</ul>
        <div className="row"><button className="secondary" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - 10))}>Vorherige</button>
          <button className="secondary" disabled={offset + 10 >= events.data.total} onClick={() => setOffset(offset + 10)}>Weitere Ereignisse</button></div>
      </> : <Status error={events?.error} />}
    </Panel>
  </>
}

createRoot(document.getElementById('root')!).render(<App />)

