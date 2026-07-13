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

interface LoggedExercise { exerciseId: number; name: string; sessions: number }
interface ProgressPoint { date: string; maxLoadKg: number; volumeKg: number }
interface VolumePoint { weekStart: string; volumeKg: number }
interface WeightPoint { date: string; weightKg: number }

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
    <section className="bg-white dark:bg-slate-800 rounded-xl shadow p-5">
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
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      api('/api/progress/exercises').then((r) => (r.ok ? r.json() : [])),
      api('/api/progress/volume').then((r) => (r.ok ? r.json() : [])),
      api('/api/progress/weight').then((r) => (r.ok ? r.json() : [])),
    ]).then(([exs, vol, wgt]) => {
      setExercises(exs)
      setVolume(vol)
      setWeight(wgt)
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
      <h1 className="text-2xl font-bold text-slate-900 dark:text-white">Seu progresso</h1>

      {isEmpty && (
        <div className="bg-white dark:bg-slate-800 rounded-xl shadow p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Nenhum dado ainda.</p>
          <p className="text-sm mt-1">
            <Link to="/registrar" className="text-emerald-600 hover:underline">Registre seu primeiro treino</Link>{' '}
            para acompanhar sua evolução.
          </p>
        </div>
      )}

      {exercises.length > 0 && (
        <ChartCard title="Progressão de carga" subtitle="Carga máxima por sessão (kg)">
          <select
            className="mb-3 rounded-lg border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-700 px-3 py-1.5 text-sm text-slate-900 dark:text-white"
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
    </div>
  )
}
