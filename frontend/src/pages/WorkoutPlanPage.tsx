import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, pollJob } from '../lib/api'

interface WorkoutPlan {
  id: string
  name: string
  split: string
  version: number
  days: {
    id: string
    order: number
    label: string
    exercises: {
      id: string
      exerciseName: string
      muscleGroup: string
      sets: number
      repsMin: number
      repsMax: number
      restSeconds: number
      notes: string | null
    }[]
  }[]
}

export default function WorkoutPlanPage() {
  const [plan, setPlan] = useState<WorkoutPlan | null>(null)
  const [loading, setLoading] = useState(true)
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    const response = await api('/api/workout-plans/active')
    setPlan(response.ok ? await response.json() : null)
    setLoading(false)
  }, [])

  useEffect(() => {
    load()
  }, [load])

  async function generate() {
    setError(null)
    setGenerating(true)
    try {
      const response = await api('/api/workout-plans/generate', { method: 'POST' })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao iniciar a geração.')
        return
      }
      const { jobId } = await response.json()
      const job = await pollJob(jobId)
      if (job.status === 'Failed') {
        setError(job.lastError ?? 'A geração falhou.')
        return
      }
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro inesperado.')
    } finally {
      setGenerating(false)
    }
  }

  if (loading) return <p className="text-slate-500">Carregando…</p>

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <h1 className="text-2xl font-bold text-slate-900 dark:text-white">
          {plan ? `${plan.name}` : 'Seu treino'}
        </h1>
        <button onClick={generate} disabled={generating}
          className="rounded-lg bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 text-white font-medium px-4 py-2 text-sm">
          {generating ? 'Gerando… (pode levar até 1 min)' : plan ? 'Regenerar treino' : 'Gerar treino'}
        </button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {!plan && !generating && (
        <div className="bg-white dark:bg-slate-800 rounded-xl shadow p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Você ainda não tem um treino ativo.</p>
          <p className="text-sm mt-1">
            Complete o <Link to="/perfil" className="text-emerald-600 hover:underline">perfil</Link> e
            clique em "Gerar treino".
          </p>
        </div>
      )}

      {plan?.days.map((day) => (
        <section key={day.id} className="bg-white dark:bg-slate-800 rounded-xl shadow overflow-hidden">
          <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white bg-slate-50 dark:bg-slate-700/50">
            {day.label}
          </h2>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-slate-500 dark:text-slate-400">
                <tr className="border-b border-slate-200 dark:border-slate-700">
                  <th className="px-5 py-2 font-medium">Exercício</th>
                  <th className="px-3 py-2 font-medium">Séries</th>
                  <th className="px-3 py-2 font-medium">Reps</th>
                  <th className="px-3 py-2 font-medium">Descanso</th>
                  <th className="px-3 py-2 font-medium hidden sm:table-cell">Obs.</th>
                </tr>
              </thead>
              <tbody className="text-slate-700 dark:text-slate-200">
                {day.exercises.map((e) => (
                  <tr key={e.id} className="border-b border-slate-100 dark:border-slate-700/50 last:border-0">
                    <td className="px-5 py-2.5">
                      {e.exerciseName}
                      <span className="block text-xs text-slate-400">{e.muscleGroup}</span>
                    </td>
                    <td className="px-3 py-2.5">{e.sets}</td>
                    <td className="px-3 py-2.5">{e.repsMin}–{e.repsMax}</td>
                    <td className="px-3 py-2.5">{e.restSeconds}s</td>
                    <td className="px-3 py-2.5 hidden sm:table-cell text-slate-400 text-xs">{e.notes}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </div>
  )
}
