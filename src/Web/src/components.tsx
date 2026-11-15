import { useEffect, useRef, type ReactNode } from 'react'
import * as echarts from 'echarts/core'
import { BarChart, LineChart, HeatmapChart, PieChart, ScatterChart, SankeyChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent, DataZoomComponent, VisualMapComponent, MarkAreaComponent, GraphicComponent,
  CalendarComponent, MarkLineComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsCoreOption } from 'echarts/core'
import type { Battery, Period } from './api'

echarts.use([BarChart, LineChart, HeatmapChart, PieChart, ScatterChart, SankeyChart, GridComponent, TooltipComponent, LegendComponent,
  DataZoomComponent, VisualMapComponent, MarkAreaComponent, GraphicComponent, CalendarComponent, MarkLineComponent, CanvasRenderer])
export const colors = ['#b67715', '#165eaa', '#a8324e', '#008269', '#6d4dac', '#397577', '#5c7065']
export const number = (value: number | null | undefined, digits = 2) =>
  value == null ? '—' : value.toLocaleString('de-DE', { maximumFractionDigits: digits })
export const power = (kw: number) => Math.abs(kw) < 10 ? number(kw * 1000, 3) + ' W' : number(kw, 3) + ' kW'
export const pct = (value: number | null | undefined) => value == null ? '—' : number(value * 100, 1) + ' %'
export const date = (iso: string) => new Date(iso.slice(0, 10) + 'T12:00:00Z').toLocaleDateString('de-DE', { timeZone: 'UTC' })
export const localDay = (iso: string, zone: string) => new Intl.DateTimeFormat('sv-SE', { timeZone: zone }).format(new Date(iso))
export function eventBounds(start: string, end: string, zone: string) {
  const first = localDay(start, zone)
  const last = localDay(new Date(Date.parse(end) - 1).toISOString(), zone)
  return { first, exclusive: last === first ? null : new Date(Date.parse(last) + 86400000).toISOString().slice(0, 10) }
}
export const stamp = (iso: string, zone: string) => new Date(iso).toLocaleString('de-DE', { timeZone: zone, timeZoneName: 'short' })
export const bounds = (p: Period) => {
  const last = new Date(Date.parse(p.end) - 86400000).toISOString().slice(0, 10)
  return p.start === last ? date(p.start) : date(p.start) + ' bis ' + date(last)
}

/** True when the page renders with the dark palette (system setting or explicit choice). */
export function darkMode() {
  const forced = document.documentElement.dataset.theme
  return forced ? forced === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches
}

export function Chart({ option, label, click, className = '', height }: {
  option: EChartsCoreOption; label: string; click?: (name: string) => void; className?: string; height?: number
}) {
  const target = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!target.current) return
    const chart = echarts.init(target.current, darkMode() ? 'dark' : undefined)
    // ECharts 6 places legends at the bottom by default; the grids here reserve space at the top.
    const legend = (option as { legend?: Record<string, unknown> }).legend
    const placed = legend && !Array.isArray(legend) && !('top' in legend) && !('bottom' in legend) ? { ...option, legend: { top: 0, ...legend } } : option
    chart.setOption({ color: colors, backgroundColor: 'transparent', textStyle: { fontFamily: 'system-ui' }, ...placed })
    if (click) chart.on('click', p => {
      const value = 'value' in p && Array.isArray(p.value) ? p.value[0] : null
      if ('name' in p && typeof p.name === 'string' && p.name) click(p.name)
      else if (typeof value === 'string') click(value)
    })
    const observer = new ResizeObserver(() => chart.resize())
    observer.observe(target.current)
    return () => { observer.disconnect(); chart.dispose() }
  }, [option, click])
  return <div ref={target} className={'chart ' + className} style={height ? { height } : undefined} role="img" aria-label={label} />
}

export function chartOptions(names: string[], unit: string, series: { name: string; data: (number | null)[]; type?: string;
  yAxisIndex?: number; markArea?: object }[], zoom = false) {
  return { animation: false, legend: { type: 'scroll' }, tooltip: { trigger: 'axis', renderMode: 'richText' },
    grid: { left: 58, right: zoom ? 55 : 24, top: 48, bottom: zoom ? 65 : 45 },
    xAxis: { type: 'category', data: names }, yAxis: zoom ? [{ type: 'value', name: unit === 'kW' ? 'Leistung' : unit,
      ...(unit === 'kW' ? { axisLabel: { formatter: power } } : {}) },
      { type: 'value', name: 'SOC %', min: 0, max: 100 }] : { type: 'value', name: unit },
    dataZoom: zoom ? [{ type: 'inside' }, { type: 'slider' }] : [],
    series: series.map(s => ({ type: 'bar', showSymbol: false, connectNulls: false, ...s,
      ...(unit === 'kW' ? { tooltip: { valueFormatter: (value: unknown) => typeof value !== 'number' ? '—' :
        s.yAxisIndex === 1 ? number(value, 1) + ' %' : power(value) } } : {}) })) }
}

export function Panel({ title, children }: { title: string; children: ReactNode }) {
  return <section className="panel"><h2>{title}</h2>{children}</section>
}
export function Status({ error, text = 'Wird geladen …' }: { error?: string; text?: string }) {
  return <p className={error ? 'notice error' : 'notice'} role={error ? 'alert' : 'status'}>{error || text}</p>
}
export function Numeric({ label, value, change, min = 0, max, step = 'any' }: {
  label: string; value: number; change: (value: number) => void; min?: number; max?: number; step?: string
}) {
  return <label>{label}<input type="number" min={min} max={max} step={step} value={value}
    onChange={e => change(e.target.valueAsNumber || 0)} /></label>
}


export function BatteryFields({ value, change, capacity = true }: { value: Battery; change: (value: Battery) => void; capacity?: boolean }) {
  const fields = [
    ...(capacity ? [['capacity', 'Kapazität vor Reserve (kWh)', 500]] : []),
    ['chargeKw', 'Maximale Ladeleistung (kW)', 500], ['dischargeKw', 'Maximale Entladeleistung (kW)', 500],
    ['chargeEfficiency', 'Ladewirkungsgrad (0–1)', 1], ['dischargeEfficiency', 'Entladewirkungsgrad (0–1)', 1],
    ['minSoc', 'Untere SOC-Grenze (0–1)', 1], ['maxSoc', 'Obere SOC-Grenze (0–1)', 1],
  ] as [keyof Battery, string, number][]
  return <div className="fields">{fields.map(([key, label, max]) =>
    <Numeric key={key} label={label} value={value[key] ?? 0} max={max} change={v => change({ ...value, [key]: v })} />)}</div>
}


/** Fixed colours per meaning, shared by tiles and charts. */
export const tones = { pv: '#e3a33a', house: '#165eaa', grid: '#a8324e', battery: '#6d4dac', export: '#008269', money: '#185840', neutral: '#5c7065' }
const paths: Record<string, string> = {
  pv: 'M12 4V2m0 20v-2m8-8h2M2 12h2m13.66-5.66 1.41-1.41M4.93 19.07l1.41-1.41m0-11.32L4.93 4.93m14.14 14.14-1.41-1.41M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10Z',
  house: 'M3 11 12 4l9 7M5 10v10h14V10M10 20v-6h4v6',
  grid: 'M13 2 4 14h7l-1 8 9-12h-7l1-8Z',
  battery: 'M7 6h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2Zm3-2h4M9 13h6',
  export: 'M12 19V5m-6 6 6-6 6 6',
  money: 'M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6',
  ratio: 'M19 5 5 19M7 9a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm10 10a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z',
  cycle: 'M21 12a9 9 0 1 1-3-6.7L21 8m0-5v5h-5',
}
export function Icon({ name }: { name: keyof typeof paths | string }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d={paths[name] ?? paths.ratio} /></svg>
}

/**
 * One key figure: label with icon, value, optional change against a reference and a short note. The explanation
 * opens on demand, so tiles stay readable while the definition remains one click away.
 */
export function Tile({ label, value, icon, tone, note, info, delta }: {
  label: string; value: string; icon: string; tone: string; note?: ReactNode; info?: string;
  delta?: { text: string; direction: 'up' | 'down' | 'flat' } | null
}) {
  return <article className="tile" style={{ ['--tone' as string]: tone }}>
    <div className="tile-head"><Icon name={icon} /><span>{label}</span></div>
    <strong>{value}</strong>
    {delta && <span className={'delta ' + delta.direction}>{delta.text}</span>}
    {note && <small>{note}</small>}
    {info && <details className="info"><summary aria-label={'Erklärung zu ' + label}>ⓘ</summary><p>{info}</p></details>}
  </article>
}

/** Relative change with a direction that already accounts for whether more is better. */
export function change(current: number | null | undefined, previous: number | null | undefined, moreIsBetter: boolean, label: string, percentPoints = false) {
  if (current == null || previous == null || (!percentPoints && previous === 0)) return null
  const diff = percentPoints ? (current - previous) * 100 : (current - previous) / Math.abs(previous) * 100
  if (Math.abs(diff) < .5) return { text: '± 0 ' + (percentPoints ? 'PP' : '%') + ' ' + label, direction: 'flat' as const }
  const better = diff > 0 === moreIsBetter
  return { text: (diff > 0 ? '+' : '−') + number(Math.abs(diff), percentPoints ? 1 : 0) + (percentPoints ? ' PP ' : ' % ') + label,
    direction: better ? 'up' as const : 'down' as const }
}
