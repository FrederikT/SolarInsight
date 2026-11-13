import { lazy, Suspense, useCallback, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { request, session, useData, type EnergyEvent, type Period, type Plant, type Report } from './api'
import { bounds, Chart, chartOptions, date, eventBounds, localDay, Numeric, number, Panel, pct, power, stamp, Status } from './components'
import { Overview } from './Overview'
import { PeriodFields } from './PeriodFields'
import { SettingsView } from './Settings'
import { duration, ImportForm } from './Provider'
import { connectionLabel, type ProviderStatus } from './ProviderSetup'
import { LiveView } from './LiveView'
import { StoragePotential } from './StoragePotential'
import { DataQuality } from './DataQuality'
import './style.css'

const ScenarioEditor = lazy(() => import('./Scenario').then(m => ({ default: m.ScenarioEditor })))

const pages = [['overview', 'Übersicht'], ['potential', 'Speicher & Potenzial'], ['status', 'Datenstatus'], ['settings', 'Einstellungen']]
const kinds = [['day', 'Tag'], ['week', 'Woche'], ['month', 'Monat'], ['quarter', 'Quartal'],
  ['season', 'Jahreszeit'], ['year', 'Jahr'], ['all', 'Seit Datenbeginn'], ['custom', 'Frei']]
const iso = (d: Date) => d.toISOString().slice(0, 10)
const plusDay = (s: string, count: number) => iso(new Date(Date.parse(s) + count * 86400000))
type Session = Awaited<ReturnType<typeof session>>
const importStates: Record<string, string> = { pending: 'Eingeplant', running: 'Läuft', completed: 'Abgeschlossen',
  failed: 'Fehlgeschlagen', cancelled: 'Abgebrochen' }
function importHelp(error: string) {
  if (error === 'no-data') return 'Sungrow hat keine verwertbaren Messwerte geliefert. Prüfe in iSolarCloud einen abgeschlossenen Tag und vergleiche ihn mit der historischen Probe in den Einstellungen.'
  if (error === 'auth') return 'Sungrow hat den Zugriff abgelehnt. Autorisiere die Anlage in den Einstellungen erneut und plane danach einen Nachimport ein.'
  if (error === 'key-restore') return 'Die Autorisierung kann nicht gelesen werden. Stelle den ursprünglichen Schlüsselbestand wieder her oder autorisiere Sungrow erneut.'
  if (['rate-limit', 'timeout', 'network', 'unavailable'].includes(error))
    return 'Sungrow war vorübergehend nicht erreichbar oder das Abruflimit wurde erreicht. Prüfe Verbindung und Kontingent und plane später einen Nachimport ein.'
  return 'Prüfe in den Einstellungen die Sungrow-Diagnose und das bestätigte Betreiberprofil, bevor du den Zeitraum erneut importierst.'
}

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
  const [liveVersion, setLiveVersion] = useState(0)
  const refreshLive = useCallback(() => setLiveVersion(v => v + 1), [])
  const [statusVersion, refreshStatus] = useState(0)
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
  const status = useData<{ report: Report; latest: string | null; lastSuccess: string | null; provider: string; progress: null | { percent: number; remainingWindows: number; seconds: number | null }; job: null | {
    id: string; windows: number; emptyWindows: number; readingRows: number; aggregateRows: number; cancelled: string | null; status: string; checkpoint: string; start: string; end: string; error: string | null; lastAttempt: string | null; lastSuccess: string | null };
    repair: { start: string; end: string; kind: string; attempts: number; due: string; open: boolean; error?: string | null }[];
    intervalCounts?: { total: number; pv: number; load: number; both: number };
    gaps: { start: string; end: string }[] }>('/data-status?' + query, version + statusVersion)
  const connection = useData<ProviderStatus>(!demo && page === 'status' ? '/provider/status' : null, version)
  const runningImport = status?.data?.job?.status === 'pending' || status?.data?.job?.status === 'running'
  useEffect(() => {
    if (!runningImport && (demo || page !== 'status')) return
    const timer = window.setInterval(() => runningImport ? refresh(v => v + 1) : refreshStatus(v => v + 1),
      runningImport ? 5000 : 30000)
    return () => window.clearInterval(timer)
  }, [demo, page, runningImport])
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
        {page === 'overview' && <LiveView zone={zone} version={liveVersion} refresh={refreshLive} />}
        {page !== 'settings' && <div className="period row">
          {detail ? <button className="secondary" onClick={() => update({ detail: null, detailEnd: null })}>Zurück zum Zeitraum</button> :
            <label>Zeitraum<select aria-label="Zeitraum" value={kind} onChange={e => update({ kind: e.target.value })}>{kinds.map(([id, label]) =>
              <option key={id} value={id}>{label}</option>)}</select></label>}
          {!detailEnd && (kind !== 'all' && kind !== 'custom' || day) ? <>
            <button className="secondary" aria-label="Vorheriger Zeitraum" onClick={() => shift(-1)}>←</button>
            {!day && ['week', 'month', 'quarter', 'season', 'year'].includes(kind) ? <PeriodFields kind={kind} anchor={anchor}
              change={value => update({ anchor: value })} /> : <label>Stichtag<input type="date" value={day || anchor} onChange={e => e.target.value && update(day ?
                { detail: e.target.value } : { anchor: e.target.value })} /></label>}
            <button className="secondary" aria-label="Nächster Zeitraum" onClick={() => shift(1)}>→</button>
          </> : !detailEnd && kind === 'custom' && <><label>Beginn<input type="date" value={start} onChange={e => e.target.value && update({ start: e.target.value })} /></label>
            <label>Ende exklusiv<input type="date" value={end} onChange={e => e.target.value && update({ end: e.target.value })} /></label></>}
          {status?.data && <span>{bounds(status.data.report.period)}</span>}
          {page === 'overview' && <button className="secondary" onClick={() => update({
            detail: null, detailEnd: null, kind: 'day', anchor: localDay(new Date().toISOString(), zone)
          })}>Heutiges Datum öffnen</button>}
        </div>}
        {runningImport && status.data?.progress && <Status text={`Datenimport · ${status.data.progress.percent} % verarbeitet`} />}
        {page === 'overview' && <><Overview query={query} drill={drill} day={day} evidence={!!detailEnd} simulation={simulation}
          version={version} liveVersion={liveVersion} /></>}
        {page === 'potential' && <><h1>Speicher & Potenzial</h1>
          <p>Wähle oben den Zeitraum. Zuerst steht eine Abschätzung für zusätzliche Speicherkapazität aus den Tageswerten.
            Darunter folgen die Auswertung der Einspeisung für eine zusätzliche Last (etwa ein Elektroauto) und die
            detaillierte Intervallsimulation, sofern die Messdaten dafür ausreichen.</p>
          <StoragePotential query={query} />
          <Surplus query={query} zone={zone} drill={drill} />
          <details className="panel"><summary>Detaillierte Intervallsimulation (Speicher, Lastverschiebung, Zusatzbedarf)</summary>
          {status?.data?.report && plant.data.capabilities.simulation ? <Suspense fallback={<Status />}><ScenarioEditor period={status.data.report.period}
            simulation={simulation} setSimulation={setSimulation} drill={drill} /></Suspense> : <Status error={status?.error}
              text={plant.data.capabilities.simulation ? 'Zeitraum wird geladen …' : 'Die Intervallsimulation rechnet jede Viertelstunde nach und braucht dafür PV und Hausverbrauch im selben Zeitraster sowie bestätigte AC-Energiepfade. Für diese Anlage ist das noch nicht bestätigt. Die Abschätzung oben nutzt stattdessen die Tageswerte.'} />}
          </details></>}
        {page === 'status' && <><h1>Datenstatus</h1>{status?.data ? <>
          <Panel title="Import und Verbindung">
            <dl className="facts">
              <div><dt>Verbindung</dt><dd>{demo ? status.data.provider : connection?.data ? connectionLabel(connection.data) :
                connection?.error ? 'Status nicht verfügbar' : 'Wird geprüft …'}</dd></div>
              <div><dt>Letzter erfolgreicher Datenabruf</dt><dd>{status.data.lastSuccess ?
                stamp(status.data.lastSuccess, zone) : 'Noch kein erfolgreicher Abruf'}</dd></div>
              <div><dt>Import</dt><dd>{status.data.job ? importStates[status.data.job.status] : 'Kein Importauftrag'}</dd></div>
            </dl>
            {!demo && connection?.error && <Status error={connection.error} />}
            {!demo && connection?.data && (!connection.data.configured || !connection.data.authorized || !connection.data.activeMapping) &&
              <div className="row actions"><p>Öffne die Einstellungen für den nächsten Einrichtungsschritt.</p>
                <button className="secondary" onClick={() => update({ page: 'settings' })}>Verbindung einrichten</button></div>}
            {status.data.job && <details><summary>Importdetails</summary><dl className="facts">{Object.entries({
              'Abgebrochen am': status.data.job.cancelled && stamp(status.data.job.cancelled, zone),
              Importbeginn: stamp(status.data.job.start, zone), Importende: stamp(status.data.job.end, zone),
              'Verarbeitet bis': stamp(status.data.job.checkpoint, zone), 'Letzter Versuch': status.data.job.lastAttempt && stamp(status.data.job.lastAttempt, zone),
              'Zuletzt Messwerte verarbeitet': status.data.job.error !== 'no-data' && status.data.job.lastSuccess
                ? stamp(status.data.job.lastSuccess, zone) : null, Fehlerkategorie: status.data.job.error,
            }).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v || '—'}</dd></div>)}</dl></details>}
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
            {status.data.job?.error && <Status error={importHelp(status.data.job.error)} />}
            {status.data.job?.status === 'completed' && status.data.job.windows > 0 &&
              status.data.job.readingRows === 0 && status.data.job.aggregateRows === 0 &&
              <p>Der Import hat keine Datensätze geändert. Die gelieferten Werte können bereits gespeichert sein.
                Leere Zeitfenster stehen in den Importdetails.</p>}
            <details><summary>Datenstand</summary><p>Jüngster Messzeitpunkt {status.data.latest ? stamp(status.data.latest, zone) : '—'} · Revision {status.data.report.revision}</p></details>
            {!demo && plant.data.capabilities.energy && !runningImport && <details><summary>Nachimport und Testimport</summary>
              <ImportForm testAvailable={plant.data.capabilities.timeseries} changed={() => refresh(v => v + 1)} /></details>}</Panel>
          {!!status.data.repair.length && <Panel title="Historische Abstimmung"><details><summary>{status.data.repair.length} offene oder geplante Zeitfenster im Zeitraum</summary>
            <ul>{status.data.repair.map(gap => <li key={gap.kind + gap.start}>{stamp(gap.start, zone)} bis {stamp(gap.end, zone)} ·
              {gap.kind === 'daily' ? ' Tageswerte' : ' Minutenwerte'} · {gap.open ? 'offen nach ' + gap.attempts + ' Versuchen' :
                'eingeplant ab ' + stamp(gap.due, zone)}{gap.error === 'incomplete-history' && ' · Quelle lieferte keine vollständige Reihe'}</li>)}</ul>
          </details></Panel>}
          <DataQuality />
          <Panel title="Abdeckung und Datenlücken im gewählten Zeitraum">
            <p>Gemeinsame Abdeckung von PV und Hausverbrauch {pct(status.data.report.metrics.coverage)}
              {status.data.report.resolutionMinutes != null ? ' · Auflösung ' + number(status.data.report.resolutionMinutes) + ' Minuten' :
                status.data.report.providerMetrics ? ' · Sungrow-Tageswerte' : ''}</p>
            {status.data.intervalCounts && <p>{status.data.intervalCounts.total} gespeicherte Intervalle · {status.data.intervalCounts.load} mit Hausverbrauch ·
              {' '}{status.data.intervalCounts.pv} mit PV · {status.data.intervalCounts.both} mit beiden Kanälen</p>}
            {!plant.data.capabilities.timeseries ? <p>Minutendaten benötigen ein bestätigtes Anlagenprofil.</p> :
              status.data.gaps.length ? <details><summary>Lücken der gemeinsamen PV- und Verbrauchsreihe anzeigen</summary>
              <p>In diesen Zeiträumen fehlt mindestens einer der beiden Kanäle. Andere Messwerte können vorhanden sein.</p>
              <ul>{status.data.gaps.map(g => <li key={g.start}>{stamp(g.start, zone)} bis {stamp(g.end, zone)}</li>)}</ul></details> :
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
  const result = useData<{ period: Period; export: number | null; captured: number | null; coverage: number; assumption: string;
    dailyExport: number | null; days: { day: string; export: number; captured: number }[];
    months: { month: string; export: number; captured: number; hours: number }[];
    seasons: { start: string; export: number; captured: number; hours: number }[];
    hoursOfDay: { hour: number; export: number | null }[] }>(
    '/surplus?' + query + '&kw=' + kw + '&from=' + Math.round(from * 60) + '&to=' + Math.round(to * 60))
  const events = useData<{ events: EnergyEvent[]; total: number }>('/events?' + query + '&offset=' + offset + '&limit=10&threshold=' +
    threshold + (type ? '&type=' + encodeURIComponent(type) : ''))
  return <>
    <Panel title="Einspeisung und Laden mit Überschuss">
      <p>Trage die maximale Ladeleistung und das Zeitfenster ein, in dem das Auto angeschlossen wäre. Die Auswertung
        begrenzt die nutzbare Energie auf die gemessene Einspeisung in diesem Fenster und auf die eingegebene Leistung.</p>
      <div className="fields"><Numeric label="Maximale Aufnahmeleistung (kW)" value={kw} change={setKw} max={100} />
        <Numeric label="Lokale Stunde ab" value={from} change={setFrom} max={23} /><Numeric label="Lokale Stunde bis" value={to} change={setTo} max={24} /></div>
      {result?.data ? <>
        {result.data.dailyExport != null && <p>Sungrow-Tageswerte für Einspeisung {number(result.data.dailyExport)} kWh.</p>}
        <p>In Messintervallen erfasste Einspeisung {number(result.data.export)} kWh · Davon im Ladefenster nutzbar {number(result.data.captured)} kWh · Abdeckung {pct(result.data.coverage)}</p>
        {result.data.export == null && <p>Für die Ladeanalyse fehlen zeitlich zugeordnete Einspeisemessungen.</p>}
        <p>{result.data.assumption} Die Werte zeigen das Potenzial bei eingestecktem Fahrzeug. Fahrbedarf und Ladeverluste sind nicht enthalten.</p>
        {result.data.hoursOfDay.some(hour => hour.export != null) && <Chart label="Gemessene Einspeisung nach lokaler Stunde"
          option={chartOptions(result.data.hoursOfDay.map(hour => String(hour.hour).padStart(2, '0') + ':00'), 'kWh',
            [{ name: 'Einspeisung nach Uhrzeit', data: result.data.hoursOfDay.map(hour => hour.export) }])} />}
        {!!result.data.months.length && <div className="table"><table><thead><tr><th>Monat</th><th>Einspeisung (kWh)</th>
          <th>Im Ladefenster nutzbar (kWh)</th><th>Erfasste Stunden</th></tr></thead><tbody>
          {result.data.months.map(month => <tr key={month.month}><td>{month.month}</td><td>{number(month.export)}</td>
            <td>{number(month.captured)}</td><td>{number(month.hours)}</td></tr>)}
        </tbody></table></div>}
        {!!result.data.seasons.length && <details><summary>Verteilung nach Jahreszeiten</summary><div className="table"><table>
          <thead><tr><th>Beginn der Jahreszeit</th><th>Einspeisung (kWh)</th><th>Im Ladefenster nutzbar (kWh)</th><th>Erfasste Stunden</th></tr></thead>
          <tbody>{result.data.seasons.map(season => <tr key={season.start}><td>{date(season.start)}</td>
            <td>{number(season.export)}</td><td>{number(season.captured)}</td><td>{number(season.hours)}</td></tr>)}</tbody>
        </table></div></details>}
        {!!result.data.days.length && <details><summary>Verteilung nach Tagen</summary><div className="table"><table>
          <thead><tr><th>Tag</th><th>Einspeisung (kWh)</th><th>Im Ladefenster nutzbar (kWh)</th></tr></thead><tbody>
            {result.data.days.map(row => <tr key={row.day}><td><button className="link" onClick={() => drill(row.day)}>{date(row.day)}</button></td>
              <td>{number(row.export)}</td><td>{number(row.captured)}</td></tr>)}
          </tbody></table></div></details>}
      </> : <Status error={result?.error} />}
    </Panel>
    <Panel title="Speicher- und Überschussereignisse">
      <p>Diese Liste zeigt Zeitabschnitte, in denen ein Messwert die gewählte Leistungsschwelle erreicht. Speicherereignisse
        benötigen zusätzlich einen passenden Ladezustand und bestätigte Speichergrenzen.</p>
      <div className="fields"><Numeric label="Ereignisschwelle (kW)" value={threshold} change={v => { setOffset(0); setThreshold(v) }} min={.001} max={100} />
        <label>Ereignistyp<select value={type} onChange={e => { setOffset(0); setType(e.target.value) }}>
          {['', 'Überschussepisode', 'Einspeisung an oberer Ladegrenze', 'Netzbezug an unterer Ladegrenze',
            'Einspeisung bei freiem Speicher', 'Hohe Last bei geringer PV-Erzeugung'].map(t => <option key={t} value={t}>{t || 'Alle'}</option>)}</select></label></div>
      {events?.data ? <><p>{events.data.total} Ereignisse. Einspeisung bei freiem Speicher ist eine Beobachtung ohne Ursachenzuordnung.</p>
        <ul>{events.data.events.map(e => <li key={e.type + e.start}><button className="link" onClick={() => { const p = eventBounds(e.start, e.end, zone); drill(p.first, p.exclusive) }}>
          {date(localDay(e.start, zone))}</button> · {e.text} Spitze {power(e.peakKw)}.</li>)}</ul>
        <div className="row"><button className="secondary" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - 10))}>Vorherige</button>
          <button className="secondary" disabled={offset + 10 >= events.data.total} onClick={() => setOffset(offset + 10)}>Weitere Ereignisse</button></div>
      </> : <Status error={events?.error} />}
    </Panel>
  </>
}

createRoot(document.getElementById('root')!).render(<App />)

