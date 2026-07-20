import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { api } from '../lib/api'
import WeeklyReportCard from '../components/WeeklyReportCard'

interface LoggedExercise { exerciseId: number; name: string; sessions: number }
interface ProgressPoint { date: string; maxLoadKg: number; volumeKg: number }
interface VolumePoint { weekStart: string; volumeKg: number }
interface WeightPoint { date: string; weightKg: number }
interface Suggestion {
  exerciseId: number
  exerciseName: string
  dayLabel: string
  repsMax: number
  action: 'Start' | 'Increase' | 'ProgressReps' | 'Consolidate'
  nextLoadKg: number | null
  incrementKg: number
  lastSets: { reps: number; loadKg: number }[]
}
interface ExerciseRecord {
  exerciseId: number
  name: string
  maxLoadKg: number
  maxLoadDate: string
  bestE1RmKg: number | null
  e1RmReps: number | null
  e1RmLoadKg: number | null
  e1RmDate: string | null
}

/** Recorde batido nos últimos 14 dias ganha destaque de "novo". */
function isRecent(iso: string | null) {
  if (!iso) return false
  const [y, m, d] = iso.split('-').map(Number)
  return Date.now() - new Date(y, m - 1, d).getTime() < 14 * 24 * 60 * 60 * 1000
}

const axisStyle = { fontSize: 12, fill: 'var(--viz-axis)' }

function formatDate(iso: string) {
  const [, month, day] = iso.split('-')
  return `${day}/${month}`
}

function ChartCard({ title, subtitle, children }: {
  title: string
  subtitle?: string
  children: React.ReactNode
}) {
  return (
    <section className="card p-5">
      <h2 className="font-semibold text-slate-900 dark:text-white">{title}</h2>
      {subtitle && <p className="text-xs text-slate-500 dark:text-slate-400 mb-2">{subtitle}</p>}
      {children}
    </section>
  )
}

export default function DashboardPage() {
  const [exercises, setExercises] = useState<LoggedExercise[]>([])
  const [selectedExercise, setSelectedExercise] = useState<number | null>(null)
  const [progress, setProgress] = useState<ProgressPoint[]>([])
  const [volume, setVolume] = useState<VolumePoint[]>([])
  const [weight, setWeight] = useState<WeightPoint[]>([])
  const [suggestions, setSuggestions] = useState<Suggestion[]>([])
  const [records, setRecords] = useState<ExerciseRecord[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      api('/api/progress/exercises').then((r) => (r.ok ? r.json() : [])),
      api('/api/progress/volume').then((r) => (r.ok ? r.json() : [])),
      api('/api/progress/weight').then((r) => (r.ok ? r.json() : [])),
      api('/api/progress/suggestions').then((r) => (r.ok ? r.json() : [])),
      api('/api/progress/records').then((r) => (r.ok ? r.json() : [])),
    ]).then(([exs, vol, wgt, sug, recs]) => {
      setExercises(exs)
      setVolume(vol)
      setWeight(wgt)
      setSuggestions(sug)
      setRecords(recs)
      if (exs.length > 0) setSelectedExercise(exs[0].exerciseId)
      setLoading(false)
    })
  }, [])

  useEffect(() => {
    if (selectedExercise === null) return
    api(`/api/progress/exercises/${selectedExercise}`).then(async (r) => {
      if (r.ok) setProgress(await r.json())
    })
  }, [selectedExercise])

  if (loading) return <p className="text-slate-500">Carregando…</p>

  const isEmpty = exercises.length === 0 && weight.length === 0

  return (
    <div className="space-y-6">
      <h1 className="page-title">Seu progresso</h1>

      <WeeklyReportCard />

      {isEmpty && (
        <div className="card p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Nenhum dado ainda.</p>
          <p className="text-sm mt-1">
            <Link to="/registrar" className="text-emerald-600 hover:underline">Registre seu primeiro treino</Link>{' '}
            para acompanhar sua evolução.
          </p>
        </div>
      )}

      {suggestions.some((s) => s.action === 'Increase') && (
        <section className="card overflow-hidden">
          <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg flex justify-between items-center">
            <span>Sugestões de progressão</span>
            <Link to="/treinar" className="text-sm font-normal text-emerald-600 hover:underline">
              Treinar agora
            </Link>
          </h2>
          <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
            {suggestions
              .filter((s) => s.action === 'Increase')
              .map((s) => {
                const lastLoad =
                  s.lastSets.length > 0 ? Math.max(...s.lastSets.map((x) => x.loadKg)) : null
                return (
                  <li key={`${s.dayLabel}-${s.exerciseId}`} className="px-5 py-2.5 flex justify-between items-center gap-3">
                    <span className="text-slate-700 dark:text-slate-200">
                      {s.exerciseName}
                      <span className="ml-2 text-xs text-slate-400">{s.dayLabel}</span>
                    </span>
                    <span className="text-xs text-emerald-700 dark:text-emerald-400 whitespace-nowrap">
                      {lastLoad} kg → <strong>{s.nextLoadKg} kg</strong> (fechou as {s.repsMax} reps)
                    </span>
                  </li>
                )
              })}
          </ul>
        </section>
      )}

      {exercises.length > 0 && (
        <ChartCard title="Progressão de carga" subtitle="Carga máxima por sessão (kg)">
          <select
            className="mb-3 field px-3 py-1.5 text-sm text-slate-900 dark:text-white"
            value={selectedExercise ?? ''}
            onChange={(e) => setSelectedExercise(Number(e.target.value))}
          >
            {exercises.map((e) => (
              <option key={e.exerciseId} value={e.exerciseId}>
                {e.name} ({e.sessions} sessões)
              </option>
            ))}
          </select>
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={progress} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
                <CartesianGrid stroke="var(--viz-grid)" vertical={false} />
                <XAxis dataKey="date" tickFormatter={formatDate} tick={axisStyle} tickLine={false} axisLine={false} />
                <YAxis unit=" kg" tick={axisStyle} tickLine={false} axisLine={false} width={64} />
                <Tooltip
                  labelFormatter={(l) => formatDate(String(l))}
                  formatter={(value) => [`${value} kg`, 'Carga máx.']}
                />
                <Line type="monotone" dataKey="maxLoadKg" name="Carga máx." stroke="var(--viz-series-1)"
                  strokeWidth={2} dot={{ r: 3 }} activeDot={{ r: 5 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </ChartCard>
      )}

      {volume.length > 0 && (
        <ChartCard title="Volume semanal" subtitle="Soma de repetições × carga por semana (kg)">
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={volume} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
                <CartesianGrid stroke="var(--viz-grid)" vertical={false} />
                <XAxis dataKey="weekStart" tickFormatter={formatDate} tick={axisStyle} tickLine={false} axisLine={false} />
                <YAxis tick={axisStyle} tickLine={false} axisLine={false} width={64} />
                <Tooltip
                  labelFormatter={(l) => `Semana de ${formatDate(String(l))}`}
                  formatter={(value) => [`${Number(value).toLocaleString('pt-BR')} kg`, 'Volume']}
                  cursor={{ fill: 'var(--viz-grid)', opacity: 0.4 }}
                />
                <Bar dataKey="volumeKg" name="Volume" fill="var(--viz-series-2)" radius={[4, 4, 0, 0]} maxBarSize={40} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </ChartCard>
      )}

      {weight.length > 0 && (
        <ChartCard title="Peso corporal" subtitle="Evolução do peso (kg)">
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={weight} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
                <CartesianGrid stroke="var(--viz-grid)" vertical={false} />
                <XAxis dataKey="date" tickFormatter={formatDate} tick={axisStyle} tickLine={false} axisLine={false} />
                <YAxis unit=" kg" domain={['auto', 'auto']} tick={axisStyle} tickLine={false} axisLine={false} width={64} />
                <Tooltip
                  labelFormatter={(l) => formatDate(String(l))}
                  formatter={(value) => [`${value} kg`, 'Peso']}
                />
                <Line type="monotone" dataKey="weightKg" name="Peso" stroke="var(--viz-series-3)"
                  strokeWidth={2} dot={{ r: 3 }} activeDot={{ r: 5 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </ChartCard>
      )}

      {records.length > 0 && (
        <section className="card overflow-hidden">
          <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg">
            Recordes pessoais
          </h2>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-slate-500 dark:text-slate-400 text-xs">
                <tr className="border-b border-slate-100 dark:border-white/[0.06]">
                  <th className="px-5 py-2 font-medium">Exercício</th>
                  <th className="px-3 py-2 font-medium text-right">Carga máx.</th>
                  <th className="px-3 py-2 font-medium text-right">1RM estimado</th>
                  <th className="px-5 py-2 font-medium text-right">Quando</th>
                </tr>
              </thead>
              <tbody className="text-slate-700 dark:text-slate-200">
                {records.slice(0, 10).map((r) => {
                  const date = r.e1RmDate ?? r.maxLoadDate
                  return (
                    <tr key={r.exerciseId} className="border-b border-slate-100 dark:border-white/[0.06] last:border-0">
                      <td className="px-5 py-2">
                        {r.name}
                        {isRecent(date) && (
                          <span className="ml-2 text-xs text-emerald-600 bg-emerald-500/10 rounded-full px-2 py-0.5">
                            novo
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-right whitespace-nowrap">{r.maxLoadKg} kg</td>
                      <td className="px-3 py-2 text-right whitespace-nowrap">
                        {r.bestE1RmKg != null ? (
                          <span title={`${r.e1RmReps} × ${r.e1RmLoadKg} kg (Epley)`}>{r.bestE1RmKg} kg</span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="px-5 py-2 text-right text-xs text-slate-400 whitespace-nowrap">
                        {formatDate(date)}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <p className="px-5 py-2 text-xs text-slate-400">
            1RM estimado pela fórmula de Epley sobre séries de até 12 repetições — referência, não convite ao teste real.
          </p>
        </section>
      )}
    </div>
  )
}
