import { useState } from 'react'
import { request, useData, type Battery, type Plant, type Rule, type Settings, type Tariff } from './api'
import { BatteryFields, date, Numeric, number, Panel, Status } from './components'
import { ProviderSetup } from './Provider'
import { LiveSetup } from './LiveView'

const ruleDefaults: Rule = { kw: .1, minutes: 15, socTolerance: .02, highLoadKw: null }
const emptyBattery: Battery = { capacity: 0, chargeKw: 0, dischargeKw: 0, chargeEfficiency: 1, dischargeEfficiency: 1, minSoc: 0, maxSoc: 1 }
const tariffNames = ['Bezug', 'Einspeisung', 'Monatlicher Grundpreis']

export function SettingsView({ demo, plant, logout, onChange }: { demo: boolean; plant: Plant; logout: () => Promise<void>; onChange: () => void }) {
  const [version, refresh] = useState(0)
  const settings = useData<Settings>('/settings/plant', version)
  const tariffs = useData<Tariff[]>('/tariffs', version)
  const changed = () => { refresh(n => n + 1); onChange() }
  return <>
    <h1>Einstellungen</h1>
    <Panel title="Verbindung">
      <p>Region EU · iSolarCloud Open API</p>
      {demo ? <p>Diese Instanz zeigt synthetische Demodaten. Anlagen- und Tarifparameter gelten für alle Besucher.</p> :
        <><p>Für Live-Daten sind eine freigegebene Entwickleranwendung, die Autorisierung durch den Anlageneigentümer und ein bestätigter Messpunktvertrag erforderlich.</p>
          <a href="https://academy.sungrow-emea.com/de/articles/331036-sungrow-s-api-developer-portal" rel="noreferrer">Sungrow-Anleitung zur Entwicklerfreigabe</a><ProviderSetup changed={changed} /></>}
      {demo && <LiveSetup zone={plant.zone} demo />}
    </Panel>
    <Panel title="Anlage und Speicher">
      {plant.devices.length > 0 && <ul>{plant.devices.map((device, i) => <li key={i}>{device.type} · {device.model}</li>)}</ul>}
      {settings?.data ? <PlantSettings key={version} data={settings.data} demo={demo} changed={changed} /> : <Status error={settings?.error} />}
    </Panel>
    <Panel title="Tarife">
      {tariffs?.data ? <TariffSettings rows={tariffs.data} demo={demo} changed={changed} /> : <Status error={tariffs?.error} />}
    </Panel>
    {!demo && <Panel title="Lokaler Zugang"><PasswordForm logout={logout} /></Panel>}
  </>
}

function PlantSettings({ data, demo, changed }: { data: Settings; demo: boolean; changed: () => void }) {
  const first = data.configurations[0]
  const [name, setName] = useState(data.name)
  const [zone, setZone] = useState(data.zone)
  const [since, setSince] = useState(first?.since || new Date().toISOString().slice(0, 10))
  const [pvKwp, setPv] = useState(first?.pvKwp || 0)
  const [confirmed, setConfirmed] = useState(!!first?.battery)
  const [battery, setBattery] = useState(first?.battery ?? emptyBattery)
  const [rule, setRule] = useState(data.rule ?? ruleDefaults)
  const [error, setError] = useState('')
  return <>
    <p>{data.name} · {data.zone}</p>
    <ul>{data.configurations.map(c => <li key={c.since}>Seit {date(c.since)} · {number(c.pvKwp)} kWp · {c.battery ?
      number(c.battery.capacity * (c.battery.maxSoc - c.battery.minSoc)) + ' kWh nutzbar' : 'Speicherparameter offen'} · {c.origin === 'synthetic' ? 'Synthetisch' : 'Manuell'}</li>)}</ul>
    {!demo && <form onSubmit={async e => {
      e.preventDefault(); setError('')
      try { await request('/settings/plant', 'PUT', { name, zone, since, pvKwp, battery: confirmed ? battery : null, rule }); changed() }
      catch (err) { setError((err as Error).message) }
    }}>
      <div className="fields"><label>Anlagenname<input value={name} onChange={e => setName(e.target.value)} required maxLength={100} /></label>
        <label>IANA-Zeitzone<input value={zone} onChange={e => setZone(e.target.value)} required /></label>
        <label>Gültig ab<input type="date" value={since} onChange={e => setSince(e.target.value)} required /></label>
        <Numeric label="PV-Nennleistung (kWp)" value={pvKwp} change={setPv} max={1000} /></div>
      <label className="check"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} /> Speicherparameter manuell bestätigen</label>
      {confirmed && <BatteryFields value={battery} change={setBattery} />}
      <h3>Analyseschwellen</h3>
      <div className="fields">
        <Numeric label="Leistungsschwelle (kW)" value={rule.kw} change={kw => setRule({ ...rule, kw })} min={.001} max={100} />
        <Numeric label="SOC-Toleranz (Prozentpunkte)" value={rule.socTolerance * 100} change={v => setRule({ ...rule, socTolerance: v / 100 })} max={25} />
        <Numeric label="Mindestdauer (Minuten)" value={rule.minutes} change={minutes => setRule({ ...rule, minutes })} min={1} max={1440} step="1" />
        <Numeric label="Hohe Last (kW, 0 = aus)" value={rule.highLoadKw ?? 0} change={v => setRule({ ...rule, highLoadKw: v || null })} max={100} />
      </div>
      {error && <Status error={error} />}<button>Anlagenparameter speichern</button>
    </form>}
  </>
}

function TariffSettings({ rows, demo, changed }: { rows: Tariff[]; demo: boolean; changed: () => void }) {
  const [edit, setEdit] = useState<Tariff>()
  const [review, setReview] = useState<'save' | 'delete'>()
  const [error, setError] = useState('')
  const previous = rows.find(t => t.id === edit?.id)
  const describe = (t: Tariff) => tariffNames[t.kind] + ' · ' + date(t.start) + ' bis ' +
    (t.end ? date(t.end) + ' (exklusiv)' : 'offen') + ' · ' + number(t.price * (t.kind === 2 ? 1 : 100)) +
    (t.kind === 2 ? ' €/Monat' : ' ct/kWh')
  const gap = [0, 1].some(kind => {
    const type = rows.filter(t => t.kind === kind)
    return !type.length || type.at(-1)?.end != null || type.some((t, i) => i > 0 && type[i - 1].end !== t.start)
  })
  return <>
    <p>Beginn inklusive, Ende exklusiv um Mitternacht in der Anlagenzeitzone. Preise wie tatsächlich bezahlt oder erhalten.</p>
    {gap && <Status text="Tariflücken vorhanden. Kosten werden nur für abgedeckte Intervalle ausgewiesen." />}
    <ul>{rows.map(t => <li key={t.id}>{describe(t)} {!demo && <button type="button" className="link" onClick={() => {
      setEdit(t); setReview(undefined); setError('')
    }}>Bearbeiten</button>}</li>)}</ul>
    {!demo && <button className="secondary" onClick={() => { setEdit({ id: '', kind: 0, start: '', end: null, price: 0 }); setReview(undefined) }}>Tarif hinzufügen</button>}
    {edit && !review && <form onSubmit={e => { e.preventDefault(); setReview('save') }}>
      <div className="fields">
        <label>Tarifart<select value={edit.kind} onChange={e => setEdit({ ...edit, kind: Number(e.target.value) })}>
          {tariffNames.map((name, i) => <option key={name} value={i}>{name}</option>)}</select></label>
        <label>Beginn<input type="date" value={edit.start} onChange={e => setEdit({ ...edit, start: e.target.value })} required /></label>
        <label>Ende exklusiv<input type="date" value={edit.end || ''} onChange={e => setEdit({ ...edit, end: e.target.value || null })} /></label>
        <Numeric label={edit.kind === 2 ? 'Preis (EUR/Monat)' : 'Preis (ct/kWh)'} value={edit.price * (edit.kind === 2 ? 1 : 100)}
          change={v => setEdit({ ...edit, price: v / (edit.kind === 2 ? 1 : 100) })} />
      </div>
      <div className="row actions"><button>Änderung prüfen</button>{previous && <button type="button" className="secondary" onClick={() => setReview('delete')}>Tarif löschen</button>}
        <button type="button" className="secondary" onClick={() => setEdit(undefined)}>Abbrechen</button></div>
    </form>}
    {edit && review && <div className="notice"><p>Vorher · {previous ? describe(previous) : 'Kein Tarif'}</p>
      <p>Nachher · {review === 'save' ? describe(edit) : 'Tarif entfällt'}</p>
      <p>Für die angezeigten Gültigkeitszeiträume werden die Kosten neu berechnet.</p>
      <div className="row"><button onClick={async () => {
        setError('')
        try {
          await request('/tariffs' + (edit.id ? '/' + edit.id : ''), review === 'delete' ? 'DELETE' : edit.id ? 'PUT' : 'POST',
            review === 'delete' ? undefined : { kind: edit.kind, start: edit.start, end: edit.end, price: edit.price })
          setEdit(undefined); setReview(undefined); changed()
        } catch (err) { setError((err as Error).message) }
      }}>Änderung speichern</button><button className="secondary" onClick={() => setReview(undefined)}>Zurück</button></div>
    </div>}
    {error && <Status error={error} />}
  </>
}

function PasswordForm({ logout }: { logout: () => Promise<void> }) {
  const [error, setError] = useState('')
  return <form onSubmit={async e => {
    e.preventDefault()
    const data = new FormData(e.currentTarget)
    try { await request('/auth/password', 'PUT', { current: data.get('current'), next: data.get('next') }); await logout() }
    catch (err) { setError((err as Error).message) }
  }}>
    <div className="fields"><label>Aktuelles Passwort<input name="current" type="password" autoComplete="current-password" required /></label>
      <label>Neues Passwort<input name="next" type="password" autoComplete="new-password" minLength={14} maxLength={256} required /></label></div>
    {error && <Status error={error} />}<button>Passwort ändern</button>
  </form>
}

