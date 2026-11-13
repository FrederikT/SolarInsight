const months = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember']
const seasons = ['Frühling', 'Sommer', 'Herbst', 'Winter']
const iso = (date: Date) => date.toISOString().slice(0, 10)
const at = (year: number, month: number, day = 1) => new Date(Date.UTC(year, month, day))
const addDays = (date: Date, days: number) => at(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + days)

function monday(year: number) {
  const jan4 = at(year, 0, 4)
  return addDays(jan4, -((jan4.getUTCDay() + 6) % 7))
}
function weeks(year: number) { return Math.round((+monday(year + 1) - +monday(year)) / 604800000) }

export function PeriodFields({ kind, anchor, change }: { kind: string; anchor: string; change: (value: string) => void }) {
  const current = new Date(anchor + 'T00:00:00Z')
  const month = current.getUTCMonth()
  const thursday = addDays(current, 3 - (current.getUTCDay() + 6) % 7)
  const year = kind === 'week' ? thursday.getUTCFullYear() : current.getUTCFullYear() - Number(kind === 'season' && month < 2)
  const index = kind === 'week' ? Math.floor((+thursday - +monday(year)) / 604800000) :
    kind === 'quarter' ? Math.floor(month / 3) : kind === 'season' ? (Math.floor((month + 1) / 3) + 3) % 4 : month
  const choices = kind === 'week' ? Array.from({ length: weeks(year) }, (_, i) => 'KW ' + (i + 1)) :
    kind === 'quarter' ? ['Q1', 'Q2', 'Q3', 'Q4'] : kind === 'season' ? seasons : months
  const choose = (y: number, i: number) => change(iso(kind === 'year' ? at(y, 0) :
    kind === 'week' ? addDays(monday(y), Math.min(i, weeks(y) - 1) * 7) :
    at(y, kind === 'quarter' ? i * 3 : kind === 'season' ? 2 + i * 3 : i)))
  const label = ({ week: 'Kalenderwoche', month: 'Monat', quarter: 'Quartal', season: 'Jahreszeit' } as Record<string, string>)[kind]
  const title = choices[index] + ' ' + (kind === 'season' && index === 3 ? year + '/' + String(year + 1).slice(-2) : year)
  // A calendar year is always evaluated as a whole; a reference date would only be confusing.
  if (kind === 'year') return <>
    <label>Jahr<select aria-label="Jahr" value={year} onChange={e => choose(Number(e.target.value), 0)}>
      {Array.from({ length: 100 }, (_, i) => 2000 + i).map(y => <option key={y}>{y}</option>)}
    </select></label>
    <strong>{year}</strong>
  </>
  return <>
    <label>{label}<select aria-label={label} value={index} onChange={e => choose(year, Number(e.target.value))}>
      {choices.map((choice, i) => <option key={choice} value={i}>{choice}</option>)}
    </select></label>
    <label>{kind === 'week' ? 'ISO-Wochenjahr' : 'Jahr'}<select value={year} onChange={e => choose(Number(e.target.value), index)}>
      {Array.from({ length: 100 }, (_, i) => 2000 + i).map(y => <option key={y}>{y}</option>)}
    </select></label>
    <strong>{title}</strong>
  </>
}
