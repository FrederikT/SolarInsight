import { useEffect, useState } from 'react'

export const base = '/api/v1'
let csrf = ''
export async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch(base + path, {
    method,
    headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), 'X-CSRF': csrf },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}))
    throw new Error(problem.detail || (response.status === 401 ? 'Anmeldung erforderlich oder Passwort ungültig.' :
      response.status === 403 ? 'Diese Änderung ist im Demomodus gesperrt.' : 'Anfrage fehlgeschlagen (' + response.status + ').'))
  }
  return response.status === 204 ? undefined as T : response.json()
}
export async function session() {
  const result = await request<{ demo: boolean; authenticated: boolean; csrf: string }>('/auth/session')
  csrf = result.csrf
  return result
}
export function useData<T>(path: string | null, version = 0) {
  const [state, set] = useState<{ path: string; data?: T; error?: string }>()
  useEffect(() => {
    if (!path) return
    let active = true
    request<T>(path).then(data => { if (active) set({ path, data }) })
      .catch(error => { if (active) set({ path, error: error.message }) })
    return () => { active = false }
  }, [path, version])
  return state?.path === path ? state : undefined
}

export type Period = { start: string; end: string }
export type Plant = { name: string; zone: string; firstDay?: string | null; region: string; synthetic: boolean; defaultAnchor: string; providerStatus: string;
  capabilities: { energy: boolean; timeseries: boolean; events: boolean; simulation: boolean }; devices: { model: string; type: string }[] }
export type Channel = { kwh: number | null; coverage: number }
export type Metrics = Record<'pv' | 'load' | 'import' | 'export' | 'charge' | 'discharge', Channel> &
  { autarky: number | null; retention: number | null; coverage: number; socMean: number | null; socMin: number | null; socMax: number | null;
    autarkyCoverage: number; retentionCoverage: number }
export type Cost = { net: number | null; coveredNet: number; import: number; export: number; fixed: number;
  importCoverage: number; exportCoverage: number; complete: boolean }
export type Report = { period: Period; timeZone: string; revision: string; resolutionMinutes: number; metrics: Metrics;
  counterGaps?: { channel: 'pv' | 'load' | 'import' | 'export' | 'charge' | 'discharge'; start: string; end: string; kwh: number }[];
  historicalSoc?: { at: string; value: number }[];
  cost: Cost; incomplete: boolean; warnings: string[]; providerMetrics: Metrics | null; providerExportDifference: number | null }
export type Point = { day?: string; start?: string; local?: string; pv: number | null; load: number | null;
  import: number | null; export: number | null; charge: number | null; discharge: number | null; soc: number | null; cost?: number | null;
  socMin?: number | null; socMax?: number | null; autarky?: number | null; retention?: number | null; coverage?: number | null }
export type EnergyEvent = { type: string; start: string; end: string; kwh: number; peakKw: number; text: string }
export type Battery = { capacity: number; chargeKw: number; dischargeKw: number; chargeEfficiency: number;
  dischargeEfficiency: number; minSoc: number; maxSoc: number; usable?: number }
export type Comparison = { pv: number; load: number; import: number; export: number; loss: number; end: number;
  autarky: number | null; cost: Cost }
export type Daily = { day: string; measuredImport: number | null; referenceImport: number; scenarioImport: number;
  measuredExport: number | null; referenceExport: number; scenarioExport: number }
export type Potential = { reference: Comparison; scenario: Comparison; measured: Metrics; validation: {
  importError: number; exportError: number; normalizedError: number; socMaePoints: number | null; sufficient: boolean; socValidated: boolean
}; savings: number | null; paybackYears: number | null; paybackStatus: string; coverage: number; resets: number;
  initialReference: number; initialScenario: number; warnings: string[];
  curve: { capacity: number; avoidedImport: number; savings: number | null; marginalImport: number | null }[];
  daily: Daily[]; additional: null | Record<string, number | null>; moves: { source: string; target: string; kwh: number }[] }
export type ModelPoint = { start: string; importKw: number; exportKw: number; soc: number; loadKw: number }
export type Job = { state: string; progress: number; error: string | null; result: Potential | null; model: ModelPoint[] | null }
export type Rule = { kw: number; socTolerance: number; minutes: number; highLoadKw: number | null }
export type Config = { since: string; pvKwp: number; origin: string; battery: Battery | null }
export type Settings = { name: string; zone: string; region: string; rule: Rule | null; configurations: Config[] }
export type Tariff = { id: string; kind: number; start: string; end: string | null; price: number }

