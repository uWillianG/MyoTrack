import { useCallback, useEffect, useState } from 'react'
import { api, watchJob } from '../lib/api'

interface Metrics {
  weekStart: string
  weekEnd: string
  workouts: {
    sessions: number
    plannedPerWeek: number | null
    volumeKg: number
    previousVolumeKg: number | null
    prs: string[]
  }
  nutrition: {
    daysLogged: number
    avgKcal: number | null
    targetKcal: number | null
    avgProteinG: number | null
    targetProteinG: number | null
  }
  weight: { startKg: number | null; endKg: number | null }
}

interface Narrative {
  summary: string
  highlights: string[]
  recommendations: string[]
}

interface Report {
  id: string
  weekStart: string
  createdAt: string
  metrics: Metrics
  narrative: Narrative | null
}

function formatShort(iso: string) {
  const [, month, day] = iso.split('-')
  return `${day}/${month}`
}

function Chip({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-xl bg-slate-50/80 px-3 py-2 dark:bg-white/[0.04]">
      <p className="text-[11px] text-slate-500 dark:text-slate-400">{label}</p>
      <p className="text-sm font-bold text-slate-900 dark:text-white">
        {value}
        {hint && <span className="ml-1 font-normal text-xs text-slate-400">{hint}</span>}
      </p>
    </div>
  )
}

/** Resumo da semana no topo do dashboard: números do sistema + narrativa do coach IA. */
export default function WeeklyReportCard() {
  const [reports, setReports] = useState<Report[]>([])
  const [selected, setSelected] = useState(0)
  const [loaded, setLoaded] = useState(false)
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    const response = await api('/api/reports/weekly')
    if (response.ok) {
      setReports(await response.json())
      setSelected(0)
    }
    setLoaded(true)
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function generate() {
    setError(null)
    setGenerating(true)
    try {
      const response = await api('/api/reports/weekly/generate', { method: 'POST' })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao gerar o relatório.')
        return
      }
      const { jobId } = await response.json()
      const job = await watchJob(jobId)
      if (job.status === 'Failed') {
        setError(job.lastError ?? 'A geração do relatório falhou.')
        return
      }
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro inesperado.')
    } finally {
      setGenerating(false)
    }
  }

  if (!loaded) return null

  if (reports.length === 0)
    return (
      <section className="card p-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="font-semibold text-slate-900 dark:text-white">Sua semana em revisão</h2>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Toda segunda-feira o coach resume seus treinos, dieta e evolução da última semana.
          </p>
          {error && <p className="mt-1 text-sm text-red-600">{error}</p>}
        </div>
        <button onClick={generate} disabled={generating} className="btn-primary px-4 py-2 text-sm">
          {generating ? 'Gerando…' : 'Gerar relatório da última semana'}
        </button>
      </section>
    )

  const report = reports[Math.min(selected, reports.length - 1)]
  const m = report.metrics
  const volumeDelta =
    m.workouts.previousVolumeKg && m.workouts.previousVolumeKg > 0
      ? Math.round(((m.workouts.volumeKg - m.workouts.previousVolumeKg) / m.workouts.previousVolumeKg) * 100)
      : null
  const weightDelta =
    m.weight.startKg != null && m.weight.endKg != null
      ? Math.round((m.weight.endKg - m.weight.startKg) * 10) / 10
      : null

  return (
    <section className="card p-5 space-y-4">
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <h2 className="font-semibold text-slate-900 dark:text-white">Sua semana em revisão</h2>
        <select
          value={report.id}
          onChange={(e) => setSelected(reports.findIndex((r) => r.id === e.target.value))}
          className="field px-3 py-1.5 text-sm text-slate-900 dark:text-white"
        >
          {reports.map((r) => (
            <option key={r.id} value={r.id}>
              {formatShort(r.metrics.weekStart)} – {formatShort(r.metrics.weekEnd)}
            </option>
          ))}
        </select>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
        <Chip
          label="Treinos"
          value={String(m.workouts.sessions)}
          hint={m.workouts.plannedPerWeek ? `de ${m.workouts.plannedPerWeek}` : undefined}
        />
        <Chip
          label="Volume"
          value={`${Math.round(m.workouts.volumeKg).toLocaleString('pt-BR')} kg`}
          hint={volumeDelta != null ? `${volumeDelta >= 0 ? '+' : ''}${volumeDelta}%` : undefined}
        />
        <Chip
          label="Dieta registrada"
          value={`${m.nutrition.daysLogged} dia${m.nutrition.daysLogged === 1 ? '' : 's'}`}
          hint={
            m.nutrition.avgKcal != null
              ? `${Math.round(m.nutrition.avgKcal)} kcal/dia${m.nutrition.targetKcal ? ` (meta ${Math.round(m.nutrition.targetKcal)})` : ''}`
              : undefined
          }
        />
        <Chip
          label="Peso"
          value={m.weight.endKg != null ? `${m.weight.endKg} kg` : '—'}
          hint={weightDelta != null ? `${weightDelta >= 0 ? '+' : ''}${weightDelta} kg` : undefined}
        />
      </div>

      {m.workouts.prs.length > 0 && (
        <p className="text-sm text-emerald-700 dark:text-emerald-400">
          🏅 Recorde{m.workouts.prs.length > 1 ? 's' : ''} da semana: {m.workouts.prs.join(', ')}
        </p>
      )}

      {report.narrative && (
        <div className="space-y-3 text-sm">
          <p className="text-slate-700 dark:text-slate-200">{report.narrative.summary}</p>
          {report.narrative.highlights.length > 0 && (
            <ul className="space-y-1">
              {report.narrative.highlights.map((h, i) => (
                <li key={i} className="text-slate-600 dark:text-slate-300">
                  <span className="text-emerald-500 mr-1.5">✦</span>
                  {h}
                </li>
              ))}
            </ul>
          )}
          {report.narrative.recommendations.length > 0 && (
            <div>
              <p className="text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">
                Para a próxima semana
              </p>
              <ul className="space-y-1">
                {report.narrative.recommendations.map((r, i) => (
                  <li key={i} className="text-slate-600 dark:text-slate-300">
                    <span className="text-slate-400 mr-1.5">{i + 1}.</span>
                    {r}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </section>
  )
}
