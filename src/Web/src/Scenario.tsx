import { useEffect, useState } from 'react'
import { base, downloadStaticExport, isStaticDemo, request, useData, type Battery, type Job, type Period, type Potential, type Settings } from './api'
import { BatteryFields, bounds, Chart, chartOptions, date, Numeric, number, Panel, pct, Status } from './components'

const baseBattery: Battery = { capacity: 14, chargeKw: 4, dischargeKw: 4, chargeEfficiency: .94,
  dischargeEfficiency: .94, minSoc: .1, maxSoc: 1 }
const defaults = { battery: false, shift: false, added: false, profile: baseBattery, dailyShift: 1, share: 20,
  targetKw: 1, sourceFrom: '00:00', sourceTo: '24:00', targetFrom: '09:00', targetTo: '17:00',
  dailyAdded: 2, addedKw: 2, addedFrom: '10:00', addedTo: '16:00', days: [0, 1, 2, 3, 4, 5, 6],
  full: false, factors: Array<number>(12).fill(1), investment: 0, curve: false, curveMax: 24, exploratory: false, empty: false }
const toMinute = (s: string) => { const [h, m] = s.split(':').map(Number); return h * 60 + m }
function Time({ label, value, change }: { label: string; value: string; change: (value: string) => void }) {
  return <label>{label}<input type="text" inputMode="numeric" placeholder="09:00" value={value} onChange={e => change(e.target.value)}
    pattern="([01][0-9]|2[0-3]):[0-5][0-9]|24:00" required /></label>
}
export function ScenarioEditor({ period, simulation, setSimulation, drill }: {
  period: Period; simulation: string | null; setSimulation: (id: string | null) => void; drill: (day: string) => void
}) {
  const [form, setForm] = useState<typeof defaults>(() => {
    try { return { ...defaults, ...JSON.parse(localStorage.getItem('solar-scenario') || '{}'), battery: false, shift: false, added: false } }
    catch { return defaults }
  })
  const [job, setJob] = useState<Job>()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [advancedBattery, setAdvancedBattery] = useState(false)
  const settings = useData<Settings>('/settings/plant')
  const battery = settings?.data?.configurations.find(c => c.since <= period.start)?.battery
  const profile = advancedBattery ? form.profile : { ...(battery ?? baseBattery), capacity: form.profile.capacity }
  const update = <K extends keyof typeof defaults>(key: K, value: typeof defaults[K]) =>
    setForm(old => ({ ...old, [key]: value }))
  useEffect(() => { localStorage.setItem('solar-scenario', JSON.stringify(form)) }, [form])
  useEffect(() => {
    if (!simulation) return
    let active = true
    let timer = 0
    const poll = async () => {
      try {
        const status = await request<Job>('/simulations/' + simulation)
        if (!active) return
        setJob(status)
        if (status.state === 'queued' || status.state === 'running') timer = window.setTimeout(poll, 700)
      } catch (e) { if (active) setError((e as Error).message) }
    }
    void poll()
    return () => { active = false; window.clearTimeout(timer) }
  }, [simulation])

  const toggle = (key: 'battery' | 'shift' | 'added', label: string) =>
    <label className="check"><input type="checkbox" checked={form[key]} onChange={e => update(key, e.target.checked)} /> {label}</label>
  const check = (key: 'full' | 'empty' | 'curve' | 'exploratory', label: string) =>
    <label className="check"><input type="checkbox" checked={form[key]} onChange={e => update(key, e.target.checked)} /> {label}</label>
  const numeric = (key: 'dailyShift' | 'share' | 'targetKw' | 'dailyAdded' | 'addedKw' | 'investment' | 'curveMax', label: string, max: number) =>
    <Numeric label={label} value={form[key]} max={max} change={v => update(key, v)} />
  const time = (key: 'sourceFrom' | 'sourceTo' | 'targetFrom' | 'targetTo' | 'addedFrom' | 'addedTo', label: string) =>
    <Time label={label} value={form[key]} change={v => update(key, v)} />
  const cancel = async () => {
    try { if (simulation) await request('/simulations/' + simulation, 'DELETE') }
    catch (e) { setError((e as Error).message) }
  }
  const running = busy || job?.state === 'running' || job?.state === 'queued'
  return <Panel title="Vergleichsszenario">
    <p>{bounds(period)} · Historischer Rückblick. Der bestehende Haushalt einschließlich seiner Speichernutzung hat Vorrang.</p>
    <form onSubmit={async e => {
      e.preventDefault(); setError(undefined); setJob(undefined); setBusy(true)
      try {
        const result = await request<{ id: string }>('/simulations', 'POST', { period,
          battery: form.battery ? profile : null, investment: form.battery && form.investment > 0 ? form.investment : null,
          shift: form.shift ? { daily: form.dailyShift, share: form.share / 100, targetKw: form.targetKw,
            source: { from: toMinute(form.sourceFrom), to: toMinute(form.sourceTo) },
            target: { from: toMinute(form.targetFrom), to: toMinute(form.targetTo) } } : null,
          added: form.added ? { daily: form.dailyAdded, kw: form.addedKw, days: form.days, full: form.full,
            monthly: form.factors, window: { from: toMinute(form.addedFrom), to: toMinute(form.addedTo) } } : null,
          curveMax: form.curve ? form.curveMax : null, exploratory: form.exploratory, emptyStart: form.empty })
        setSimulation(result.id)
      } catch (e) { setError((e as Error).message) } finally { setBusy(false) }
    }}>
      <fieldset><legend>Speicher</legend>{toggle('battery', 'Speicher ändern')}
        <p>Gib die geplante gesamte Batteriekapazität ein. Das Ergebnis zeigt vermiedenen Netzbezug und die
          modellierte Kostenänderung. Die Kapazitätskurve vergleicht mehrere Größen im selben Zeitraum.</p>
        {form.battery && <><Numeric label="Kapazität vor Reserve (kWh)" value={form.profile.capacity} max={500}
          change={capacity => update('profile', { ...profile, capacity })} />
          <p>Nutzbares Fenster {number(profile.capacity * (profile.maxSoc - profile.minSoc))} kWh</p>
          {numeric('investment', 'Investition für die Erweiterung (EUR, optional)', 1000000)}
          <details><summary>Erweiterte Speicherparameter</summary><label className="check"><input type="checkbox" checked={advancedBattery}
            onChange={e => { update('profile', profile); setAdvancedBattery(e.target.checked) }} /> Eigene Parameter verwenden</label>
            {advancedBattery && <BatteryFields value={form.profile} change={v => update('profile', v)} capacity={false} />}</details></>}
        {check('curve', 'Kapazitäts-Nutzen-Kurve berechnen')}
        {form.curve && numeric('curveMax', 'Maximale Kapazität (kWh)', 500)}
      </fieldset>
      <fieldset><legend>Verbrauchsverschiebung</legend>{toggle('shift', 'Verbrauch verschieben')}
        <p>Lege fest, wie viele kWh pro Tag in ein anderes Zeitfenster verschoben werden dürfen. Die Gesamtnachfrage bleibt gleich.</p>
        {form.shift && <><p>Obergrenze unter den gewählten Annahmen. Tatsächliche Geräteverfügbarkeit ist unbekannt.</p>
          <div className="fields">{numeric('dailyShift', 'Tagesbudget (kWh)', 100)}{time('targetFrom', 'Ziel ab')}{time('targetTo', 'Ziel bis')}</div>
          <details><summary>Erweiterte Verschiebungsparameter</summary><div className="fields">
            {numeric('share', 'Quellanteil (%)', 100)}{numeric('targetKw', 'Zusätzliche Zielleistung (kW)', 100)}
            {time('sourceFrom', 'Quelle ab')}{time('sourceTo', 'Quelle bis')}</div></details></>}
      </fieldset>
      <fieldset><legend>Zusatzbedarf</legend>{toggle('added', 'Zusatzverbrauch ergänzen')}
        <p>Für ein Elektroauto entsprechen Tagesziel, Aufnahmeleistung und Verfügbarkeitsfenster den kWh je Ladetag,
          der Ladeleistung und den Stunden am Ladepunkt.</p>
        {form.added && <><div className="fields">{numeric('dailyAdded', 'Tagesziel (kWh)', 500)}{numeric('addedKw', 'Aufnahmeleistung (kW)', 100)}
          {time('addedFrom', 'Verfügbar ab')}{time('addedTo', 'Verfügbar bis')}</div>
          {check('full', 'Bedarf vollständig decken')}
          <p>{form.full ? 'PV bevorzugen, Rest aus Speicher oder Netz.' : 'Nur Überschuss nutzen. Unerreichte Ziele bleiben ungedeckt.'}</p>
          <div className="row">{['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'].map((d, i) => <label className="check" key={d}>
            <input type="checkbox" checked={form.days.includes(i)} onChange={e => update('days', e.target.checked ?
              [...form.days, i] : form.days.filter(v => v !== i))} />{d}</label>)}</div>
          <details><summary>Monatsfaktoren für das Tagesziel</summary><div className="fields">{form.factors.map((factor, i) =>
            <Numeric key={i} label={new Intl.DateTimeFormat('de', { month: 'long' }).format(new Date(2026, i, 15))}
              value={factor} max={5} change={v => update('factors', form.factors.map((n, j) => j === i ? v : n))} />)}</div></details>
        </>}
      </fieldset>
      <details><summary>Initialisierung und Datenqualität</summary>{check('empty', 'Beide Modelle leer starten')}{check('exploratory', 'Explorativen Lauf bei geringer Datenqualität zulassen')}</details>
      <div className="row actions"><button disabled={running} type="submit">Simulation starten</button>
        {running && <button className="secondary" type="button" onClick={cancel} disabled={!simulation}>Lauf abbrechen</button>}
        <button className="secondary" type="button" disabled={running} onClick={() => { setForm(defaults); setAdvancedBattery(false); setSimulation(null); setJob(undefined); setError(undefined) }}>Zurücksetzen</button></div>
    </form>
    {(error || job?.error) && <Status error={error ?? job?.error ?? undefined} />}
    {running && <p role="status">Simulation läuft · {job?.progress ?? 0} %</p>}
    {job?.state === 'cancelled' && <Status text="Lauf abgebrochen." />}
    {job?.state === 'completed' && job.result && simulation && <Results result={job.result} id={simulation} drill={drill} />}
  </Panel>
}

function Results({ result: r, id, drill }: { result: Potential; id: string; drill: (day: string) => void }) {
  const rows = [
    ['PV (kWh)', r.measured.pv.kwh, r.reference.pv, r.scenario.pv],
    ['Verbrauch (kWh)', r.measured.load.kwh, r.reference.load, r.scenario.load],
    ['Bezug (kWh)', r.measured.import.kwh, r.reference.import, r.scenario.import],
    ['Einspeisung (kWh)', r.measured.export.kwh, r.reference.export, r.scenario.export],
    ['Speicherverluste (kWh)', null, r.reference.loss, r.scenario.loss],
    ['Endbestand (kWh)', null, r.reference.end, r.scenario.end],
    ['Autarkie (%)', r.measured.autarky == null ? null : r.measured.autarky * 100,
      r.reference.autarky == null ? null : r.reference.autarky * 100, r.scenario.autarky == null ? null : r.scenario.autarky * 100],
    ['Netto-Kosten (EUR)', null, r.reference.cost.net, r.scenario.cost.net],
  ] as [string, number | null, number | null, number | null][]
  const additional = r.additional
  return <div className="result">
    <h3>Simulationsergebnis</h3>
    <a className="button secondary" href={base + '/exports/simulations/' + id}
      onClick={isStaticDemo ? e => { e.preventDefault(); void downloadStaticExport('simulation', id, 'solar-insight-simulation.json', 'application/json') } : undefined}>JSON exportieren</a>
    <p>{r.validation.sufficient ? 'Referenz ausreichend angenähert' : 'Explorative Auswertung'} · Abdeckung {pct(r.coverage)} · normierter Fehler {pct(r.validation.normalizedError)} · SOC-MAE {number(r.validation.socMaePoints)} Prozentpunkte</p>
    <p>Referenzabweichung Modell minus Messung · Bezug {number(r.validation.importError)} kWh · Export {number(r.validation.exportError)} kWh.</p>
    <div className="table"><table><thead><tr>{['Kennzahl', 'Gemessen', 'Modellreferenz', 'Szenario', 'Delta', 'Änderung'].map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>
      {rows.map(([name, measured, reference, scenario]) => {
        const delta = reference == null || scenario == null ? null : scenario - reference
        return <tr key={name}><th>{name}</th><td>{number(measured)}</td><td>{number(reference)}</td><td>{number(scenario)}</td>
          <td>{number(delta)}{name.startsWith('Autarkie') && ' PP'}</td><td>{pct(reference && delta != null ? delta / reference : null)}</td></tr>
      })}</tbody></table></div>
    <p>Modellierte Ersparnis {number(r.savings)} € · Anfangsbestand Referenz {number(r.initialReference)} kWh · Szenario {number(r.initialScenario)} kWh.</p>
    <p>{r.paybackYears != null ? number(r.paybackYears) + ' Jahre. ' : ''}{r.paybackStatus}</p>
    {additional && <><h3>Zusatzbedarf und Energieherkunft</h3><dl className="facts">
      {Object.entries({ requested: 'Angefordert', served: 'Gedeckt', pv: 'Direkte PV', battery: 'Aus Batterie',
        openingBattery: 'Davon Anfangsbestand', grid: 'Direkter Netzanteil', unserved: 'Ungedeckt', extraGrid: 'Änderung Gesamtbezug',
        remainingExport: 'Verbleibender Export', totalCostChange: 'Kostenänderung zum Haushalt ohne Zusatzbedarf',
        savingsAgainstGrid: 'Vorteil gegenüber demselben Zusatzbedarf vollständig aus dem Netz' }).map(([k, label]) =>
        <div key={k}><dt>{label}</dt><dd>{number(additional[k])} {k.includes('Cost') || k.includes('Savings') || k === 'savingsAgainstGrid' ? '€' : 'kWh'}</dd></div>)}
    </dl><p>Der Anfangsbestand hat ungesicherte Herkunft.</p></>}
    <ul>{r.warnings.map(w => <li key={w}>{w}</li>)}</ul>
    {r.curve.length > 0 && <><h3>Kapazitäts-Nutzen-Kurve</h3>
      <Chart label="Vermiedener Netzbezug nach Kapazität" option={chartOptions(r.curve.map(p => number(p.capacity) + ' kWh'), 'kWh',
        [{ name: 'Vermiedener Bezug', type: 'line', data: r.curve.map(p => p.avoidedImport) }])} />
      <details><summary>Stützpunkte und Grenznutzen</summary><div className="table"><table><thead><tr><th>Kapazität</th><th>Vermiedener Bezug</th><th>Ersparnis</th><th>Zusatznutzen pro kWh</th></tr></thead>
        <tbody>{r.curve.map(p => <tr key={p.capacity}><td>{number(p.capacity)} kWh</td><td>{number(p.avoidedImport)} kWh</td><td>{number(p.savings)} €</td><td>{number(p.marginalImport)} kWh/kWh</td></tr>)}</tbody></table></div></details></>}
    <h3>Tagesverlauf des Netzbezugs</h3>
    <Chart label="Täglicher Bezug in Messung, Referenz und Szenario" click={drill} option={chartOptions(r.daily.map(d => d.day), 'kWh', [
      { name: 'Gemessen', data: r.daily.map(d => d.measuredImport) }, { name: 'Referenz', data: r.daily.map(d => d.referenceImport) },
      { name: 'Szenario', data: r.daily.map(d => d.scenarioImport) }])} />
    <details><summary>Einzelne Tagesabweichungen</summary><div className="table"><table><thead><tr><th>Tag</th><th>Bezug Referenz − Messung</th><th>Export Referenz − Messung</th></tr></thead>
      <tbody>{r.daily.map(d => <tr key={d.day}><td><button type="button" className="link" onClick={() => drill(d.day)}>{date(d.day)}</button></td>
        <td>{number(d.measuredImport == null ? null : d.referenceImport - d.measuredImport)} kWh</td><td>{number(d.measuredExport == null ? null : d.referenceExport - d.measuredExport)} kWh</td></tr>)}</tbody></table></div></details>
    {r.moves.length > 0 && <p>Verschoben {number(r.moves.reduce((sum, move) => sum + move.kwh, 0))} kWh. Quellen und Ziele stehen im JSON-Export.</p>}
  </div>
}

