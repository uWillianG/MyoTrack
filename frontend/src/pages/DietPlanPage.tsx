import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, watchJob } from '../lib/api'
import ReviewBadge from '../components/ReviewBadge'

interface DietPlan {
  id: string
  name: string
  calorieGoal: string
  reviewStatus: string
  reviewNote: string | null
  targets: { targetKcal: number; targetProteinG: number; targetCarbsG: number; targetFatG: number }
  totals: { kcal: number; proteinG: number; carbsG: number; fatG: number }
  meals: {
    id: string
    order: number
    name: string
    items: {
      id: string
      foodName: string
      quantityG: number
      kcal: number
      proteinG: number
      carbsG: number
      fatG: number
    }[]
  }[]
}

const goalLabels: Record<string, string> = {
  Deficit: 'Déficit calórico',
  Maintenance: 'Manutenção',
  Surplus: 'Superávit calórico',
}

export default function DietPlanPage() {
  const [plan, setPlan] = useState<DietPlan | null>(null)
  const [loading, setLoading] = useState(true)
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    const response = await api('/api/diet-plans/active')
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
      const response = await api('/api/diet-plans/generate', { method: 'POST' })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao iniciar a geração.')
        return
      }
      const { jobId } = await response.json()
      const job = await watchJob(jobId)
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
        <div className="flex items-center gap-3 flex-wrap">
          <h1 className="page-title">
            {plan ? plan.name : 'Sua dieta'}
            {plan && (
              <span className="ml-3 text-sm font-normal text-slate-500">
                {goalLabels[plan.calorieGoal] ?? plan.calorieGoal}
              </span>
            )}
          </h1>
          {plan && <ReviewBadge reviewStatus={plan.reviewStatus} reviewNote={plan.reviewNote} />}
        </div>
        <button onClick={generate} disabled={generating}
          className="btn-primary px-4 py-2 text-sm">
          {generating ? 'Gerando…' : plan ? 'Regenerar dieta' : 'Gerar dieta'}
        </button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {!plan && !generating && (
        <div className="card p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Você ainda não tem uma dieta ativa.</p>
          <p className="text-sm mt-1">
            Complete o <Link to="/perfil" className="text-emerald-600 hover:underline">perfil</Link> (com peso
            registrado) e clique em "Gerar dieta".
          </p>
        </div>
      )}

      {plan && (
        <>
          <section className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            {[
              ['Calorias', plan.totals.kcal, plan.targets.targetKcal, 'kcal'],
              ['Proteína', plan.totals.proteinG, plan.targets.targetProteinG, 'g'],
              ['Carboidrato', plan.totals.carbsG, plan.targets.targetCarbsG, 'g'],
              ['Gordura', plan.totals.fatG, plan.targets.targetFatG, 'g'],
            ].map(([label, actual, target, unit]) => (
              <div key={label as string} className="card p-4">
                <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
                <p className="text-xl font-bold text-slate-900 dark:text-white">
                  {Math.round(actual as number)}
                  <span className="text-sm font-normal text-slate-400"> / {Math.round(target as number)} {unit}</span>
                </p>
              </div>
            ))}
          </section>

          {plan.meals.map((meal) => (
            <section key={meal.id} className="card overflow-hidden">
              <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg flex justify-between">
                <span>{meal.name}</span>
                <span className="text-sm font-normal text-slate-500">
                  {Math.round(meal.items.reduce((sum, i) => sum + i.kcal, 0))} kcal
                </span>
              </h2>
              <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
                {meal.items.map((item) => (
                  <li key={item.id} className="px-5 py-2.5 flex justify-between text-slate-700 dark:text-slate-200">
                    <span>{item.foodName} — {item.quantityG} g</span>
                    <span className="text-slate-400 text-xs whitespace-nowrap ml-3">
                      P {item.proteinG}g · C {item.carbsG}g · G {item.fatG}g
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          ))}

          <p className="text-xs text-slate-400">
            Plano gerado automaticamente com base no seu perfil. Valores nutricionais são estimativas (base TACO).
            Consulte um nutricionista para acompanhamento profissional.
          </p>
        </>
      )}
    </div>
  )
}
