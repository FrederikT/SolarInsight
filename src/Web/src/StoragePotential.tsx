import { useState } from 'react'
import { useData, type Period } from './api'
import { bounds, Chart, date, number, Panel, pct, Status } from './components'

type Totals = { import: number; export: number; autarky: number | null; selfConsumption: number | null }
type Scenario = { extraCapacity: number; totals: Totals; avoidedImport: number; lostExport: number; savings: number;
  annualSavings: number | null; paybackYears: number | null; equivalentFullCycles: number;
  daysCapacityLimited: number; daysSurplusLimited: number; daysDemandLimited: number;
  months: { month: string; days: number; avoidedImport: number; lostExport: number }[] }
type Result = { period: Period; result: { days: number; usedDays: number; first: string | null; last: string | null; load: number; pv: number;
  current: Totals; scenarios: Scenario[]; notes: string[];
  existing: null | { days: number; charge: number; discharge: number; importWithout: number; exportWithout: number;
    autarkyWithout: number | null; autarkyWith: number | null; savings: number } };
  prices: { import: number; export: number; importSource: string; exportSource: string };
  battery: null | { capacity: number; usable: number; estimated: boolean; capacityEstimate: number | null; maxDailyCharge: number | null } }

const sourceLabel: Record<string, string> = { tariff: 'aus den hinterlegten Tarifen', default: 'Standardannahme, bitte anpassen', input: 'eigene Eingabe' }
const kwh = (v: number | null | undefined) => v == null ? '—' : number(v, Math.abs(v) >= 100 ? 0 : 1) + ' kWh'
const eur = (v: number | null | undefined) => v == null ? '—' : number(v, 0) + ' €'
const points = (v: number | null) => v == null ? '—' : (v >= 0 ? '+' : '') + number(v * 100, 1) + ' PP'

function Field({ label, help, value, change, step = .01, min = 0, max }: {
  label: string; help: string; value: number; change: (v: number) => void; step?: number; min?: number; max?: number
}) {
  return <label>{label}<input type="number" value={value} step={step} min={min} max={max}
    onChange={e => Number.isFinite(e.target.valueAsNumber) && change(e.target.valueAsNumber)} /><small>{help}</small></label>
}

/**
 * Long-term storage estimate from daily values. Every assumption is an input with its source shown, and the
 * result table compares the measured situation with each additional capacity.
 */
export function StoragePotential({ query }: { query: string }) {
  const [efficiency, setEfficiency] = useState(90)
  const [importPrice, setImportPrice] = useState<number | null>(null)
  const [exportPrice, setExportPrice] = useState<number | null>(null)
  const [investment, setInvestment] = useState(0)
  const [custom, setCustom] = useState(12)
  const [selected, setSelected] = useState(5)
  const extra = [...new Set([0, 2.5, 5, 7.5, 10, 15, custom])].sort((a, b) => a - b)
  const params = new URLSearchParams({ extra: extra.join(','), efficiency: String(efficiency / 100),
    ...(importPrice != null ? { importPrice: String(importPrice) } : {}), ...(exportPrice != null ? { exportPrice: String(exportPrice) } : {}),
    ...(investment > 0 ? { investment: String(investment) } : {}) })
  const state = useData<Result>('/storage-potential?' + query + '&' + params)
  const data = state?.data
  const r = data?.result
  const scenario = r?.scenarios.find(s => s.extraCapacity === selected) ?? r?.scenarios.find(s => s.extraCapacity > 0)
  return <Panel title="Was bringt ein größerer Speicher?">
    <p>Die Abschätzung nutzt die Tageswerte des oben gewählten Zeitraums (Sungrow-Tageswerte, sonst vollständige Messintervalle). Für belastbare Jahreswerte wähle ein ganzes Jahr
      oder „Seit Datenbeginn“. Ein zusätzlicher Speicher kann nur Strom aufnehmen, der bisher eingespeist wurde, und nur Netzbezug ersetzen,
      der bisher angefallen ist.</p>
    <fieldset><legend>Annahmen</legend><div className="fields">
      <Field label="Wirkungsgrad Laden und Entladen (%)" help="Anteil der eingespeicherten Energie, der wieder nutzbar ist. Typisch 85–95 %."
        value={efficiency} step={1} min={50} max={100} change={setEfficiency} />
      <Field label="Strompreis Netzbezug (€/kWh)" help={'Arbeitspreis inkl. Steuern · ' + sourceLabel[importPrice != null ? 'input' : data?.prices.importSource ?? 'default']}
        value={importPrice ?? data?.prices.import ?? .3} change={setImportPrice} />
      <Field label="Einspeisevergütung (€/kWh)" help={'Entgangener Erlös je gespeicherter kWh · ' + sourceLabel[exportPrice != null ? 'input' : data?.prices.exportSource ?? 'default']}
        value={exportPrice ?? data?.prices.export ?? .08} change={setExportPrice} />
      <Field label="Investition je kWh Zusatzkapazität (€, optional)" help="Für eine einfache Amortisationszeit. 0 = keine Angabe."
        value={investment} step={10} max={10000} change={setInvestment} />
      <Field label="Eigene Zusatzkapazität (kWh nutzbar)" help="Wird zusätzlich zu 2,5 bis 15 kWh verglichen." value={custom} step={.5} max={200} change={setCustom} />
    </div></fieldset>
    {!data ? <Status error={state?.error} text="Speicherpotenzial wird berechnet …" /> : !r || r.usedDays === 0 ?
      <Status text="Im gewählten Zeitraum fehlen Tageswerte für PV, Verbrauch, Netzbezug und Einspeisung." /> : <>
      <h3>Heutige Situation</h3>
      <dl className="facts">
        <div><dt>Ausgewertet</dt><dd>{r.usedDays} Tage</dd><small>{r.first && r.last ? date(r.first) + ' bis ' + date(r.last) : bounds(data.period)}</small></div>
        <div><dt>Hausverbrauch · PV-Ertrag</dt><dd>{kwh(r.load)} · {kwh(r.pv)}</dd></div>
        <div><dt>Netzbezug · Einspeisung</dt><dd>{kwh(r.current.import)} · {kwh(r.current.export)}</dd></div>
        <div><dt>Autarkie · Eigenverbrauch</dt><dd>{pct(r.current.autarky)} · {pct(r.current.selfConsumption)}</dd></div>
        {data.battery && <div><dt>Vorhandener Speicher</dt><dd>{number(data.battery.usable, 1)} kWh nutzbar</dd>
          <small>{data.battery.estimated ? (data.battery.capacityEstimate != null ? 'Geschätzt aus Ladezustand und Ladeenergie' :
            'Geschätzt aus der größten Tagesladung') + ', keine Speicherparameter hinterlegt' : 'Aus den Einstellungen'}</small></div>}
      </dl>
      {r.existing && <p>An {r.existing.days} Tagen mit Batteriewerten lieferte der vorhandene Speicher {kwh(r.existing.discharge)}.
        Ohne ihn wäre der Autarkiegrad an diesen Tagen etwa {pct(r.existing.autarkyWithout)} statt {pct(r.existing.autarkyWith)} gewesen,
        Wert rund {eur(r.existing.savings)} bei den gewählten Preisen.</p>}
      <h3>Szenarien mit zusätzlicher Kapazität</h3>
      <div className="table"><table><thead><tr><th>Zusätzlich</th><th>Netzbezug</th><th>Einspeisung</th><th>Autarkie</th><th>Eigenverbrauch</th>
        <th>Ersparnis im Zeitraum</th><th>Pro Jahr (hochgerechnet)</th>{investment > 0 && <th>Amortisation</th>}<th>Vollzyklen</th></tr></thead>
        <tbody>{r.scenarios.map(s => <tr key={s.extraCapacity} aria-selected={s.extraCapacity === scenario?.extraCapacity}>
          <th><button className="link" onClick={() => setSelected(s.extraCapacity)}>{s.extraCapacity === 0 ? 'Heute' : '+ ' + number(s.extraCapacity, 1) + ' kWh'}</button></th>
          <td>{kwh(s.totals.import)}{s.extraCapacity > 0 && <small> (−{kwh(s.avoidedImport)})</small>}</td>
          <td>{kwh(s.totals.export)}</td>
          <td>{pct(s.totals.autarky)}{s.extraCapacity > 0 && <small> ({points((s.totals.autarky ?? 0) - (r.current.autarky ?? 0))})</small>}</td>
          <td>{pct(s.totals.selfConsumption)}</td><td>{s.extraCapacity ? eur(s.savings) : '—'}</td>
          <td>{s.extraCapacity ? eur(s.annualSavings) : '—'}</td>
          {investment > 0 && <td>{s.paybackYears == null ? '—' : number(s.paybackYears, 0) + ' Jahre'}</td>}
          <td>{s.extraCapacity ? number(s.equivalentFullCycles, 0) : '—'}</td></tr>)}</tbody></table></div>
      <Chart label="Vermiedener Netzbezug nach Zusatzkapazität" option={{ animation: false,
        tooltip: { trigger: 'axis', renderMode: 'richText', valueFormatter: (v: unknown) => typeof v === 'number' ? number(v, 0) : '—' },
        grid: { left: 62, right: 62, top: 48, bottom: 45 }, legend: {},
        xAxis: { type: 'category', name: 'kWh', data: r.scenarios.map(s => number(s.extraCapacity, 1)) },
        yAxis: [{ type: 'value', name: 'kWh vermieden' }, { type: 'value', name: '€ pro Jahr' }],
        series: [{ name: 'Vermiedener Netzbezug', type: 'line', data: r.scenarios.map(s => s.avoidedImport), itemStyle: { color: '#a8324e' } },
          { name: 'Ersparnis pro Jahr', type: 'bar', yAxisIndex: 1, data: r.scenarios.map(s => s.annualSavings), itemStyle: { color: '#8fb3a0' } }] }} />
      <p><small>Flacht die Kurve ab, bringt jede weitere kWh Speicher weniger. Grund sind fehlende Überschüsse in den dunklen Monaten
        oder ein bereits gedeckter Nachtbedarf.</small></p>
      {scenario && scenario.extraCapacity > 0 && <>
        <h3>+ {number(scenario.extraCapacity, 1)} kWh im Jahresverlauf</h3>
        <p>An {scenario.daysCapacityLimited} Tagen begrenzte die Speichergröße den Nutzen, an {scenario.daysSurplusLimited} Tagen fehlte PV-Überschuss,
          an {scenario.daysDemandLimited} Tagen war der Netzbezug bereits vollständig ersetzt.</p>
        <Chart label="Vermiedener Netzbezug je Monat" option={{ animation: false, legend: {},
          tooltip: { trigger: 'axis', renderMode: 'richText', valueFormatter: (v: unknown) => typeof v === 'number' ? number(v, 1) + ' kWh' : '—' },
          grid: { left: 62, right: 24, top: 48, bottom: 45 }, xAxis: { type: 'category', data: scenario.months.map(m => m.month) },
          yAxis: { type: 'value', name: 'kWh' }, dataZoom: scenario.months.length > 24 ? [{ type: 'inside' }, { type: 'slider', height: 18 }] : [],
          series: [{ name: 'Vermiedener Netzbezug', type: 'bar', data: scenario.months.map(m => m.avoidedImport), itemStyle: { color: '#a8324e' } },
            { name: 'Weniger Einspeisung', type: 'bar', data: scenario.months.map(m => m.lostExport), itemStyle: { color: '#008269' } }] }} />
      </>}
      <details><summary>Rechenweg und Grenzen</summary><ul>{r.notes.map(n => <li key={n}>{n}</li>)}
        <li>Vollzyklen = vermiedener Netzbezug ÷ Zusatzkapazität im ausgewerteten Zeitraum.</li>
        <li>Hochrechnung pro Jahr = Ersparnis im Zeitraum × 365 ÷ ausgewertete Tage. Bei Zeiträumen unter einem Jahr überzeichnet das die Sommer- oder Wintermonate.</li></ul></details>
    </>}
  </Panel>
}
