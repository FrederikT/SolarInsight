import { useEffect, useRef, type ReactNode } from 'react'
import * as echarts from 'echarts/core'
import { BarChart, LineChart, HeatmapChart, PieChart, ScatterChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent, DataZoomComponent, VisualMapComponent, MarkAreaComponent, GraphicComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { EChartsCoreOption } from 'echarts/core'
import type { Battery, Period } from './api'

echarts.use([BarChart, LineChart, HeatmapChart, PieChart, ScatterChart, GridComponent, TooltipComponent, LegendComponent,
  DataZoomComponent, VisualMapComponent, MarkAreaComponent, GraphicComponent, CanvasRenderer])
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

export function Chart({ option, label, click }: { option: EChartsCoreOption; label: string; click?: (name: string) => void }) {
  const target = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!target.current) return
    const chart = echarts.init(target.current)
    chart.setOption({ color: colors, textStyle: { fontFamily: 'system-ui' }, ...option })
    if (click) chart.on('click', p => { if ('name' in p && typeof p.name === 'string') click(p.name) })
    const observer = new ResizeObserver(() => chart.resize())
    observer.observe(target.current)
    return () => { observer.disconnect(); chart.dispose() }
  }, [option, click])
  return <div ref={target} className="chart" role="img" aria-label={label} />
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

