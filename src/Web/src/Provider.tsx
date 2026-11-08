import { useState } from 'react'
import { request } from './api'
import { Status } from './components'

export function duration(seconds: number) {
  const minutes = Math.ceil(seconds / 60)
  if (minutes < 1) return 'unter einer Minute'
  const hours = Math.floor(minutes / 60)
  return hours ? `${hours} Std. ${minutes % 60} Min.` : `${minutes} Min.`
}

export function ImportForm({ changed, testAvailable }: { changed: () => void; testAvailable: boolean }) {
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
  }}>{testAvailable ? <label>Importart<select value={test ? 'test' : 'days'} disabled={busy} onChange={e => setTest(e.target.value === 'test')}>
    <option value="days">Ganze Tage</option><option value="test">Kurzer Testimport</option>
  </select></label> : <p>Der Nachimport lädt Tageswerte. Für einen kurzen Testimport ist ein bestätigter Minutenmesspunkt erforderlich.</p>}
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

