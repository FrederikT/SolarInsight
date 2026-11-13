import { useEffect, useState } from 'react'
import { request } from './api'
import { date, number } from './components'

type Day = { day: string; status: 'accepted' | 'complete' | 'rejected' | 'missing'; reason: string | null;
  values: Record<string, number>; deviations: Record<string, number>; missing: string[] }
type Status = { state: 'idle' | 'running' | 'completed' | 'cancelled' | 'failed'; apply: boolean; start: string | null; end: string | null;
  days: number; done: number; accepted: number; complete: number; rejected: number; missing: number; calls: number;
  remainingCalls: number; writtenValues: number; current: string | null; error: string | null; recent: Day[];
  totals: { charge: number; discharge: number } | null; problems: Record<string, number> }
type Info = { status: Status; spacingSeconds: number; previewDays: number }

const statusText: Record<Day['status'], string> = { accepted: 'übernehmbar', complete: 'bereits vollständig', rejected: 'verworfen', missing: 'Zählerstand fehlt' }
const names: Record<string, string> = { pv: 'PV', load: 'Verbrauch', import: 'Bezug', export: 'Einspeisung', charge: 'Ladung', discharge: 'Entladung' }
const duration = (seconds: number) => seconds < 90 ? Math.round(seconds) + ' s' : seconds < 5400 ? Math.round(seconds / 60) + ' min' : number(seconds / 3600, 1) + ' h'

/**
 * DATA-06: battery daily values from the inverter's midnight counters. Preview first; the write run only fills
 * values that are still missing, so existing CSV or API values stay untouched.
 */
export function BatteryBackfill() {
  const [info, setInfo] = useState<Info>()
  const [unavailable, setUnavailable] = useState(false)
  const [error, setError] = useState<string>()
  const [start, setStart] = useState('2023-06-01')
  const [end, setEnd] = useState('2023-06-14')
  const [confirm, setConfirm] = useState(false)
  const running = info?.status.state === 'running'

  useEffect(() => {
    let active = true
    const load = () => request<Info>('/provider/battery-history').then(i => active && setInfo(i)).catch(() => active && setUnavailable(true))
    load()
    const timer = setInterval(load, running ? 3000 : 15000)
    return () => { active = false; clearInterval(timer) }
  }, [running])

  if (unavailable) return null
  if (!info) return null
  const s = info.status
  const run = (apply: boolean) => {
    setError(undefined)
    request('/provider/battery-history', 'POST', { start, end, apply })
      .then(() => request<Info>('/provider/battery-history').then(setInfo)).catch(e => setError(e.message))
    setConfirm(false)
  }
  const span = (Date.parse(end) - Date.parse(start)) / 86_400_000 + 1
  const problems = s.recent.filter(d => d.status === 'rejected' || d.status === 'missing')
  return <section aria-label="Batteriewerte nachladen">
    <h3>Batterie-Tageswerte aus Gerätezählern nachladen</h3>
    <p>Vor August 2026 fehlen Batterieladung und -entladung als Tageswert. Die Gerätehistorie des Wechselrichters enthält aber die
      Gesamtzähler. Die Differenz der Zählerstände zweier Mitternächte ergibt die Tageswerte. Ein Tag wird nur übernommen, wenn die Bilanz
      schließt und PV, Verbrauch, Bezug und Einspeisung höchstens 0,2 kWh von den gespeicherten Tageswerten abweichen. Vorhandene Werte
      bleiben unverändert, fehlende werden ergänzt.</p>
    <div className="row">
      <label>Von<input type="date" value={start} max={end} onChange={e => setStart(e.target.value)} disabled={running} /></label>
      <label>Bis einschließlich<input type="date" value={end} min={start} onChange={e => setEnd(e.target.value)} disabled={running} /></label>
    </div>
    <p><small>Etwa {number(span + 1, 0)} Abrufe bei iSolarCloud, Dauer rund {duration((span + 1) * info.spacingSeconds)} (ein Abruf je Mitternacht,
      {' '}{info.spacingSeconds} s Abstand). Bereits vollständige Tage werden übersprungen. Die Vorschau umfasst höchstens {info.previewDays} Tage
      und speichert nichts.</small></p>
    <div className="row">
      <button disabled={running || span > info.previewDays || span < 1} onClick={() => run(false)}>Vorschau</button>
      {!confirm ? <button disabled={running || span < 1} onClick={() => setConfirm(true)}>Fehlende Werte übernehmen …</button> :
        <><span>Fehlende Tageswerte für {number(span, 0)} Tage speichern? Vorher eine Datenbanksicherung anlegen.</span>
          <button onClick={() => run(true)}>Ja, übernehmen</button><button className="link" onClick={() => setConfirm(false)}>Abbrechen</button></>}
      {running && <button onClick={() => request('/provider/battery-history', 'DELETE').catch(e => setError(e.message))}>Lauf abbrechen</button>}
    </div>
    {error && <p className="notice" role="alert">{error}</p>}
    {s.state !== 'idle' && s.start && s.end && <div role="status">
      <p><strong>{s.apply ? 'Übernahme' : 'Vorschau'} {date(s.start)} bis {date(s.end)}</strong> · {{ running: 'läuft', completed: 'abgeschlossen',
        cancelled: 'abgebrochen', failed: 'fehlgeschlagen', idle: '' }[s.state]}{s.current && ' · aktuell ' + date(s.current)}
        {running && s.remainingCalls > 0 && ' · noch etwa ' + duration(s.remainingCalls * info.spacingSeconds)}</p>
      <p>{s.done} von {s.days} offenen Tagen geprüft · {s.accepted} übernehmbar · {s.rejected} verworfen · {s.missing} ohne Zählerstand
        {s.complete > 0 && ' · ' + s.complete + ' bereits vollständig'} · {s.calls} Abrufe{s.apply && ' · ' + s.writtenValues + ' Werte gespeichert'}
        {s.totals && ` · Ladung ${number(s.totals.charge, 1)} kWh, Entladung ${number(s.totals.discharge, 1)} kWh`}</p>
      {s.error && <p className="notice">{s.error}</p>}
      {Object.keys(s.problems).length > 0 && <p><small>Hinweise beim Abruf: {Object.entries(s.problems).map(([k, v]) => k + ' (' + v + '×)').join(' · ')}</small></p>}
      {s.recent.length > 0 && <details open={!s.apply}><summary>Geprüfte Tage{problems.length > 0 && ` (${problems.length} mit Problemen)`}</summary>
        <div className="table"><table><thead><tr><th>Tag</th><th>Ergebnis</th><th>Ladung</th><th>Entladung</th><th>Größte Abweichung</th><th>Hinweis</th></tr></thead>
          <tbody>{s.recent.map(d => {
            const worst = Object.entries(d.deviations).sort((a, b) => Math.abs(b[1]) - Math.abs(a[1]))[0]
            return <tr key={d.day}><td>{date(d.day)}</td><td>{statusText[d.status]}</td>
              <td>{d.values.charge == null ? '—' : number(d.values.charge, 1)}</td><td>{d.values.discharge == null ? '—' : number(d.values.discharge, 1)}</td>
              <td>{worst ? (names[worst[0]] ?? worst[0]) + ' ' + number(worst[1], 2) + ' kWh' : '—'}</td>
              <td>{d.reason ?? (d.missing.length ? 'ergänzt: ' + d.missing.map(c => names[c] ?? c).join(', ') : '')}</td></tr>
          })}</tbody></table></div></details>}
    </div>}
  </section>
}
