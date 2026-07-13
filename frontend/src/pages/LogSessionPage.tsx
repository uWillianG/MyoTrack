import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../lib/api'

interface Exercise {
  id: number
  name: string
  muscleGroup: string
}

interface SetRow {
  exerciseId: number
  setNumber: number
  reps: string
  loadKg: string
}

export default function LogSessionPage() {
  const navigate = useNavigate()
  const [exercises, setExercises] = useState<Exercise[]>([])
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10))
  const [weightKg, setWeightKg] = useState('')
  const [sets, setSets] = useState<SetRow[]>([])
  const [selectedExercise, setSelectedExercise] = useState<number>(0)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    api('/api/exercises').then(async (r) => {
      if (r.ok) {
        const list = (await r.json()) as Exercise[]
        setExercises(list)
        if (list.length > 0) setSelectedExercise(list[0].id)
      }
    })
  }, [])

  function addSet() {
    const existing = sets.filter((s) => s.exerciseId === selectedExercise)
    const last = existing.at(-1)
    setSets([...sets, {
      exerciseId: selectedExercise,
      setNumber: existing.length + 1,
      reps: last?.reps ?? '10',
      loadKg: last?.loadKg ?? '20',
    }])
  }

  function updateSet(index: number, field: 'reps' | 'loadKg', value: string) {
    setSets(sets.map((s, i) => (i === index ? { ...s, [field]: value } : s)))
  }

  function removeSet(index: number) {
    setSets(sets.filter((_, i) => i !== index))
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    if (sets.length === 0) {
      setError('Adicione pelo menos uma série.')
      return
    }
    setSaving(true)
    try {
      const response = await api('/api/sessions', {
        method: 'POST',
        body: JSON.stringify({
          date,
          sets: sets.map((s) => ({
            exerciseId: s.exerciseId,
            setNumber: s.setNumber,
            reps: Number(s.reps),
            loadKg: Number(s.loadKg),
          })),
        }),
      })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao salvar a sessão.')
        return
      }
      if (weightKg) {
        await api('/api/measurements', {
          method: 'POST',
          body: JSON.stringify({ date, weightKg: Number(weightKg) }),
        })
      }
      navigate('/')
    } finally {
      setSaving(false)
    }
  }

  const exercisesById = new Map(exercises.map((e) => [e.id, e]))
  const inputClass =
    'rounded-lg border border-slate-300 dark:border-slate-600 bg-white dark:bg-slate-700 px-3 py-2 text-slate-900 dark:text-white text-sm'

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <h1 className="text-2xl font-bold text-slate-900 dark:text-white">Registrar treino</h1>

      <section className="bg-white dark:bg-slate-800 rounded-xl shadow p-6 flex flex-wrap gap-4 items-end">
        <div>
          <label className="block text-sm text-slate-600 dark:text-slate-300 mb-1">Data</label>
          <input type="date" className={inputClass} value={date} onChange={(e) => setDate(e.target.value)} />
        </div>
        <div>
          <label className="block text-sm text-slate-600 dark:text-slate-300 mb-1">Peso corporal hoje (kg, opcional)</label>
          <input type="number" step="0.1" min="30" max="300" className={inputClass} value={weightKg}
            onChange={(e) => setWeightKg(e.target.value)} placeholder="ex.: 80.5" />
        </div>
      </section>

      <section className="bg-white dark:bg-slate-800 rounded-xl shadow p-6 space-y-4">
        <div className="flex flex-wrap gap-3 items-end">
          <div className="flex-1 min-w-48">
            <label className="block text-sm text-slate-600 dark:text-slate-300 mb-1">Exercício</label>
            <select className={`${inputClass} w-full`} value={selectedExercise}
              onChange={(e) => setSelectedExercise(Number(e.target.value))}>
              {exercises.map((ex) => (
                <option key={ex.id} value={ex.id}>{ex.name} ({ex.muscleGroup})</option>
              ))}
            </select>
          </div>
          <button type="button" onClick={addSet}
            className="rounded-lg bg-slate-700 hover:bg-slate-800 text-white px-4 py-2 text-sm">
            + Adicionar série
          </button>
        </div>

        {sets.length > 0 && (
          <table className="w-full text-sm">
            <thead className="text-left text-slate-500 dark:text-slate-400">
              <tr>
                <th className="py-2 font-medium">Exercício</th>
                <th className="py-2 font-medium">Série</th>
                <th className="py-2 font-medium">Reps</th>
                <th className="py-2 font-medium">Carga (kg)</th>
                <th />
              </tr>
            </thead>
            <tbody className="text-slate-700 dark:text-slate-200">
              {sets.map((set, index) => (
                <tr key={index} className="border-t border-slate-100 dark:border-slate-700/50">
                  <td className="py-2 pr-2">{exercisesById.get(set.exerciseId)?.name}</td>
                  <td className="py-2">{set.setNumber}ª</td>
                  <td className="py-2">
                    <input type="number" min="1" max="100" className={`${inputClass} w-20`} value={set.reps}
                      onChange={(e) => updateSet(index, 'reps', e.target.value)} />
                  </td>
                  <td className="py-2">
                    <input type="number" min="0" max="1000" step="0.5" className={`${inputClass} w-24`} value={set.loadKg}
                      onChange={(e) => updateSet(index, 'loadKg', e.target.value)} />
                  </td>
                  <td className="py-2 text-right">
                    <button type="button" onClick={() => removeSet(index)}
                      className="text-red-500 hover:text-red-700 text-xs">Remover</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {error && <p className="text-sm text-red-600">{error}</p>}

      <button type="submit" disabled={saving}
        className="rounded-lg bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 text-white font-medium px-6 py-2">
        {saving ? 'Salvando…' : 'Salvar sessão'}
      </button>
    </form>
  )
}
