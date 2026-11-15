import { useData } from './api'
import { BatteryBackfill } from './BatteryBackfill'
import { date, number, Panel, Status } from './components'

type Channel = 'pv' | 'load' | 'import' | 'export' | 'charge' | 'discharge'
type Month = { month: string; days: number; daily: Record<Channel, number>;
  intervals: Record<Channel | 'total' | 'complete', number>; socSamples: number }
type Quality = { first: string | null; today: string; months: Month[]; socSamples: number;
  sources: { point: string; revision: string; days: number; first: string; last: string }[];
  gaps: Record<Channel, { start: string; end: string; days: number }[]> }

const names: Record<Channel, string> = { pv: 'PV', load: 'Verbrauch', import: 'Bezug', export: 'Einspeisung', charge: 'Ladung', discharge: 'Entladung' }
const channels = Object.keys(names) as Channel[]
const sources: Record<string, string> = { 'sungrow-day-2': 'iSolarCloud-API (Tageswerte)', 'sungrow-portal-csv-day-v1': 'Portal-CSV-Export (Tageswerte)',
  'sungrow-device-counters-v1': 'Gerätezähler (15 Minuten)', 'sungrow-minutes-v1': 'Minutenwerte aus Momentanleistung',
  'sungrow-device-counter-day-v1': 'Gerätezähler um Mitternacht (Tageswerte)' }
const shade = (share: number) => share >= .999 ? '#d7ecdf' : share > 0 ? '#fbeec8' : '#f6d9d6'

/** Monthly availability per channel plus re-import guidance, so gaps are visible before they distort analyses. */
export function DataQuality() {
  const state = useData<Quality>('/data-quality')
  if (!state?.data) return <Status error={state?.error} text="Datenbestand wird geprüft …" />
  const q = state.data
  if (!q.first) return <Panel title="Datenbestand"><p>Noch keine Messdaten gespeichert.</p></Panel>
  const lastMonth = (key: Channel) => q.gaps[key].filter(g => g.end !== q.today)
  return <Panel title="Datenbestand und Lücken">
    <p>Seit {date(q.first)} gespeichert. Grün: an allen Tagen des Monats vorhanden, gelb: teilweise, rot: fehlt.
      Der laufende Tag zählt noch nicht. Tageswerte bilden die Grundlage für Monats-, Jahres- und Potenzialauswertungen.
      15-Minuten-Intervalle werden für Uhrzeit-Auswertungen, Ladegrenzen und Ereignisse gebraucht.</p>
    <div className="table"><table><thead><tr><th>Monat</th>{channels.map(c => <th key={c}>{names[c]}</th>)}
      <th>Intervalle vollständig</th><th>SOC-Stichproben</th></tr></thead><tbody>
      {[...q.months].reverse().map(m => <tr key={m.month}><th>{m.month}</th>
        {channels.map(c => <td key={c} style={{ background: shade(m.days ? m.daily[c] / m.days : 0) }}>{m.daily[c]}/{m.days}</td>)}
        <td>{m.intervals.complete ? number(m.intervals.complete, 0) : '—'}{m.intervals.total > m.intervals.complete && <small> von {number(m.intervals.total, 0)}</small>}</td>
        <td>{m.socSamples ? number(m.socSamples, 0) : '—'}</td></tr>)}</tbody></table></div>
    <details><summary>Quellen der gespeicherten Werte</summary><ul>{q.sources.map(s => <li key={s.point + s.revision}>
      {s.point === 'intervals' ? 'Intervalle' : names[s.point as Channel] ?? s.point} · {sources[s.revision] ?? s.revision} · {s.days} Tage ·
      {' '}{date(s.first)} bis {date(s.last)}</li>)}</ul></details>
    <details><summary>Fehlende Tageswerte je Messgröße</summary><ul>{channels.map(c => {
      const gaps = lastMonth(c)
      const total = gaps.reduce((a, g) => a + g.days, 0)
      return <li key={c}><strong>{names[c]}</strong> · {total ? total + ' Tage: ' + gaps.slice(0, 12).map(g => g.days === 1 ? date(g.start) :
        date(g.start) + '–' + date(new Date(Date.parse(g.end) - 86_400_000).toISOString())).join(', ') + (gaps.length > 12 ? ' …' : '') : 'vollständig'}</li>
    })}</ul></details>
    <details><summary>Nachimport fehlender Werte</summary><ul>
      <li>Fehlende Tageswerte eines Zeitraums lassen sich unter „Nachimport und Testimport“ erneut bei iSolarCloud abfragen. Ein manueller Nachimport
        ersetzt vorhandene Tageswerte desselben Tages durch die API-Werte. Duplikate entstehen nicht, weil Messgröße und Tag eindeutig sind.</li>
      <li>Batterie-Tageswerte vor August 2026 liefert die Tagesroute der API nicht (geprüft für 2023 und 2024). Ein erneuter API-Nachimport ergänzt sie
        deshalb nicht. Der Weg ist der Nachimport aus den Gerätezählern oben. Die Portal-Monats- und -Jahresberichte enthalten keine Batteriespalten.</li>
      <li>Der Hintergrundabgleich und der Gerätezählerimport ergänzen nur fehlende Werte und überschreiben keine vorhandenen.</li>
      <li>Ladezustand und 15-Minuten-Intervalle mit allen Kanälen kommen aus dem Gerätezählerimport. Ältere Zeiträume brauchen einen gezielten Zähler-Nachimport.</li>
    </ul></details>
    <BatteryBackfill />
  </Panel>
}
