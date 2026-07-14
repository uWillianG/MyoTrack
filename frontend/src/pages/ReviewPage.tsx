import { useCallback, useEffect, useState } from 'react'
import { api, getRoles } from '../lib/api'

interface PendingWorkout {
  id: string
  name: string
  split: string
  goal: string
  version: number
  createdAt: string
  student: string
}

interface PendingDiet {
  id: string
  name: string
  calorieGoal: string
  version: number
  createdAt: string
  targetKcal: number
  student: string
}

interface WorkoutDetail {
  id: string
  name: string
  split: string
  goal: string
  days: {
    order: number
    label: string
    exercises: {
      exerciseName: string
      sets: number
      repsMin: number
      repsMax: number
      restSeconds: number
      notes: string | null
    }[]
  }[]
}

interface DietDetail {
  id: string
  name: string
  calorieGoal: string
  targets: { targetKcal: number; targetProteinG: number; targetCarbsG: number; targetFatG: number }
  meals: {
    order: number
    name: string
    items: { foodName: string; quantityG: number }[]
  }[]
}

type Kind = 'workout-plans' | 'diet-plans'

export default function ReviewPage() {
  const roles = getRoles()
  const canReviewWorkouts = roles.includes('Trainer') || roles.includes('Admin')
  const canReviewDiets = roles.includes('Nutritionist') || roles.includes('Admin')

  const [workouts, setWorkouts] = useState<PendingWorkout[]>([])
  const [diets, setDiets] = useState<PendingDiet[]>([])
  const [detail, setDetail] = useState<{ kind: Kind; workout?: WorkoutDetail; diet?: DietDetail } | null>(null)
  const [note, setNote] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    const [w, d] = await Promise.all([
      canReviewWorkouts ? api('/api/reviews/workout-plans') : Promise.resolve(null),
      canReviewDiets ? api('/api/reviews/diet-plans') : Promise.resolve(null),
    ])
    if (w?.ok) setWorkouts(await w.json())
    if (d?.ok) setDiets(await d.json())
    setLoading(false)
  }, [canReviewWorkouts, canReviewDiets])

  useEffect(() => {
    load()
  }, [load])

  async function openDetail(kind: Kind, id: string) {
    setError(null)
    setNote('')
    const response = await api(`/api/reviews/${kind}/${id}`)
    if (!response.ok) {
      setError('Falha ao carregar o plano.')
      return
    }
    const data = await response.json()
    setDetail(kind === 'workout-plans' ? { kind, workout: data } : { kind, diet: data })
  }

  async function decide(status: 'Approved' | 'ChangesRequested') {
    if (!detail) return
    const id = detail.workout?.id ?? detail.diet?.id
    if (status === 'ChangesRequested' && !note.trim()) {
      setError('Descreva os ajustes sugeridos antes de enviar.')
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      const response = await api(`/api/reviews/${detail.kind}/${id}`, {
        method: 'POST',
        body: JSON.stringify({ status, note: note.trim() || null }),
      })
      if (!response.ok) {
        setError('Falha ao registrar a revisão.')
        return
      }
      setDetail(null)
      await load()
    } finally {
      setSubmitting(false)
    }
  }

  if (!canReviewWorkouts && !canReviewDiets) {
    return <p className="text-slate-500">Você não tem permissão para revisar planos.</p>
  }
  if (loading) return <p className="text-slate-500">Carregando…</p>

  return (
    <div className="space-y-6">
      <h1 className="page-title">Revisão de planos</h1>
      {error && <p className="text-sm text-red-600">{error}</p>}

      {detail ? (
        <section className="card p-5 space-y-4">
          <div className="flex items-center justify-between flex-wrap gap-3">
            <h2 className="font-semibold text-slate-900 dark:text-white">
              {detail.workout?.name ?? detail.diet?.name}
            </h2>
            <button onClick={() => setDetail(null)} className="text-sm text-slate-500 hover:underline">
              ← Voltar à fila
            </button>
          </div>

          {detail.workout?.days.map((day) => (
            <div key={day.order}>
              <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200 mb-1">{day.label}</h3>
              <ul className="text-sm text-slate-600 dark:text-slate-300 space-y-0.5">
                {day.exercises.map((e, i) => (
                  <li key={i}>
                    {e.exerciseName} — {e.sets}×{e.repsMin}–{e.repsMax}, descanso {e.restSeconds}s
                    {e.notes && <span className="text-slate-400"> ({e.notes})</span>}
                  </li>
                ))}
              </ul>
            </div>
          ))}

          {detail.diet && (
            <>
              <p className="text-sm text-slate-500">
                Metas: {detail.diet.targets.targetKcal} kcal · P {detail.diet.targets.targetProteinG}g ·
                C {detail.diet.targets.targetCarbsG}g · G {detail.diet.targets.targetFatG}g
              </p>
              {detail.diet.meals.map((meal) => (
                <div key={meal.order}>
                  <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200 mb-1">{meal.name}</h3>
                  <ul className="text-sm text-slate-600 dark:text-slate-300 space-y-0.5">
                    {meal.items.map((item, i) => (
                      <li key={i}>{item.foodName} — {item.quantityG}g</li>
                    ))}
                  </ul>
                </div>
              ))}
            </>
          )}

          <div className="space-y-2 pt-2 border-t border-slate-200 dark:border-white/[0.08]">
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Observações para o aluno (obrigatório ao sugerir ajustes)"
              rows={3}
              className="w-full field px-3 py-2 text-sm text-slate-900 dark:text-white"
            />
            <div className="flex gap-2">
              <button
                onClick={() => decide('Approved')}
                disabled={submitting}
                className="btn-primary px-4 py-2 text-sm"
              >
                Aprovar
              </button>
              <button
                onClick={() => decide('ChangesRequested')}
                disabled={submitting}
                className="inline-flex items-center justify-center rounded-xl bg-linear-to-b from-amber-500 to-amber-600 px-4 py-2 text-sm font-semibold text-white shadow-lg shadow-amber-600/25 transition-all duration-150 hover:from-amber-400 hover:to-amber-600 active:scale-[0.98] disabled:pointer-events-none disabled:opacity-50"
              >
                Sugerir ajustes
              </button>
            </div>
          </div>
        </section>
      ) : (
        <>
          {canReviewWorkouts && (
            <section className="card overflow-hidden">
              <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg">
                Treinos aguardando revisão ({workouts.length})
              </h2>
              {workouts.length === 0 ? (
                <p className="px-5 py-4 text-sm text-slate-500">Nenhum treino pendente.</p>
              ) : (
                <ul className="divide-y divide-slate-100 dark:divide-white/[0.06]">
                  {workouts.map((p) => (
                    <li key={p.id} className="px-5 py-3 flex items-center justify-between gap-3 flex-wrap">
                      <div className="text-sm">
                        <span className="font-medium text-slate-800 dark:text-slate-100">{p.name}</span>
                        <span className="block text-xs text-slate-400">
                          {p.student} · {p.split} · v{p.version} · {new Date(p.createdAt).toLocaleDateString()}
                        </span>
                      </div>
                      <button
                        onClick={() => openDetail('workout-plans', p.id)}
                        className="text-sm text-emerald-600 hover:underline"
                      >
                        Revisar
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          )}

          {canReviewDiets && (
            <section className="card overflow-hidden">
              <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg">
                Dietas aguardando revisão ({diets.length})
              </h2>
              {diets.length === 0 ? (
                <p className="px-5 py-4 text-sm text-slate-500">Nenhuma dieta pendente.</p>
              ) : (
                <ul className="divide-y divide-slate-100 dark:divide-white/[0.06]">
                  {diets.map((p) => (
                    <li key={p.id} className="px-5 py-3 flex items-center justify-between gap-3 flex-wrap">
                      <div className="text-sm">
                        <span className="font-medium text-slate-800 dark:text-slate-100">{p.name}</span>
                        <span className="block text-xs text-slate-400">
                          {p.student} · {p.targetKcal} kcal · v{p.version} · {new Date(p.createdAt).toLocaleDateString()}
                        </span>
                      </div>
                      <button
                        onClick={() => openDetail('diet-plans', p.id)}
                        className="text-sm text-emerald-600 hover:underline"
                      >
                        Revisar
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          )}
        </>
      )}
    </div>
  )
}
