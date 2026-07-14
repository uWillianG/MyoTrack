import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../lib/api'

const muscleGroups = [
  ['Chest', 'Peito'], ['Back', 'Costas'], ['Shoulders', 'Ombros'], ['Biceps', 'Bíceps'],
  ['Triceps', 'Tríceps'], ['Quadriceps', 'Quadríceps'], ['Hamstrings', 'Posteriores'],
  ['Glutes', 'Glúteos'], ['Calves', 'Panturrilhas'], ['Abs', 'Abdômen'],
] as const

const equipmentOptions = [
  ['Barbell', 'Barra'], ['Dumbbell', 'Halteres'], ['Machine', 'Máquinas'],
  ['Cable', 'Polia'], ['Kettlebell', 'Kettlebell'], ['Bodyweight', 'Peso corporal'],
] as const

const injuryOptions = [
  ['knee', 'Joelho'], ['lower-back', 'Lombar'], ['shoulder', 'Ombro'],
  ['elbow', 'Cotovelo'], ['wrist', 'Punho'], ['hip', 'Quadril'], ['neck', 'Pescoço'],
] as const

const TERMS_VERSION = '1.0'

export default function OnboardingPage() {
  const navigate = useNavigate()
  const [form, setForm] = useState({
    birthDate: '',
    sex: 'M',
    heightCm: '',
    weightKg: '',
    biotype: '' as string,
    experienceLevel: 'Beginner',
    goal: 'Hypertrophy',
    trainingDaysPerWeek: 3,
    priorityMuscleGroups: [] as string[],
    injuryTags: [] as string[],
    injuryNotes: '',
    availableEquipment: [] as string[],
    dietaryRestrictions: '',
    foodPreferences: '',
  })
  const [consented, setConsented] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [hasProfile, setHasProfile] = useState(false)

  useEffect(() => {
    api('/api/profile').then(async (r) => {
      if (!r.ok) return
      const p = await r.json()
      setHasProfile(true)
      setForm((f) => ({
        ...f,
        birthDate: p.birthDate ?? '',
        sex: p.sex ?? 'M',
        heightCm: p.heightCm?.toString() ?? '',
        biotype: p.biotype ?? '',
        experienceLevel: p.experienceLevel,
        goal: p.goal,
        trainingDaysPerWeek: p.trainingDaysPerWeek,
        priorityMuscleGroups: p.priorityMuscleGroups ?? [],
        injuryTags: p.injuryTags ?? [],
        injuryNotes: p.injuryNotes ?? '',
        availableEquipment: p.availableEquipment ?? [],
        dietaryRestrictions: (p.dietaryRestrictions ?? []).join(', '),
        foodPreferences: (p.foodPreferences ?? []).join(', '),
      }))
    })
  }, [])

  function toggle(list: string[], value: string): string[] {
    return list.includes(value) ? list.filter((v) => v !== value) : [...list, value]
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    if (!hasProfile && !consented) {
      setError('É necessário aceitar o tratamento de dados de saúde para continuar.')
      return
    }
    setSaving(true)
    try {
      const profileResponse = await api('/api/profile', {
        method: 'PUT',
        body: JSON.stringify({
          birthDate: form.birthDate || null,
          sex: form.sex,
          heightCm: form.heightCm ? Number(form.heightCm) : null,
          biotype: form.biotype || null,
          experienceLevel: form.experienceLevel,
          goal: form.goal,
          trainingDaysPerWeek: form.trainingDaysPerWeek,
          priorityMuscleGroups: form.priorityMuscleGroups,
          injuryNotes: form.injuryNotes || null,
          injuryTags: form.injuryTags,
          availableEquipment: form.availableEquipment,
          dietaryRestrictions: form.dietaryRestrictions.split(',').map((s) => s.trim()).filter(Boolean),
          foodPreferences: form.foodPreferences.split(',').map((s) => s.trim()).filter(Boolean),
        }),
      })
      if (!profileResponse.ok) {
        const data = await profileResponse.json().catch(() => null)
        setError(data?.error ?? 'Falha ao salvar o perfil.')
        return
      }

      if (!hasProfile) {
        await api('/api/profile/consents', {
          method: 'POST',
          body: JSON.stringify([
            { type: 'HealthData', termsVersion: TERMS_VERSION },
            { type: 'TermsOfService', termsVersion: TERMS_VERSION },
          ]),
        })
      }

      if (form.weightKg) {
        await api('/api/measurements', {
          method: 'POST',
          body: JSON.stringify({
            date: new Date().toISOString().slice(0, 10),
            weightKg: Number(form.weightKg),
          }),
        })
      }

      navigate('/treino')
    } finally {
      setSaving(false)
    }
  }

  const inputClass =
    'w-full field px-3 py-2 text-slate-900 dark:text-white text-sm'
  const labelClass = 'block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1'

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <h1 className="page-title">
        {hasProfile ? 'Editar perfil' : 'Vamos montar seu perfil'}
      </h1>

      <section className="card p-6 grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label className={labelClass}>Data de nascimento</label>
          <input type="date" className={inputClass} value={form.birthDate}
            onChange={(e) => setForm({ ...form, birthDate: e.target.value })} required />
        </div>
        <div>
          <label className={labelClass}>Sexo</label>
          <select className={inputClass} value={form.sex} onChange={(e) => setForm({ ...form, sex: e.target.value })}>
            <option value="M">Masculino</option>
            <option value="F">Feminino</option>
          </select>
        </div>
        <div>
          <label className={labelClass}>Altura (cm)</label>
          <input type="number" min="100" max="250" className={inputClass} value={form.heightCm}
            onChange={(e) => setForm({ ...form, heightCm: e.target.value })} required />
        </div>
        <div>
          <label className={labelClass}>Peso atual (kg)</label>
          <input type="number" min="30" max="300" step="0.1" className={inputClass} value={form.weightKg}
            onChange={(e) => setForm({ ...form, weightKg: e.target.value })} required={!hasProfile} />
        </div>
        <div>
          <label className={labelClass}>Biotipo</label>
          <select className={inputClass} value={form.biotype} onChange={(e) => setForm({ ...form, biotype: e.target.value })}>
            <option value="">Não sei</option>
            <option value="Ectomorph">Ectomorfo (magro, dificuldade em ganhar peso)</option>
            <option value="Mesomorph">Mesomorfo (ganha músculo com facilidade)</option>
            <option value="Endomorph">Endomorfo (tendência a acumular gordura)</option>
          </select>
        </div>
        <div>
          <label className={labelClass}>Nível de experiência</label>
          <select className={inputClass} value={form.experienceLevel}
            onChange={(e) => setForm({ ...form, experienceLevel: e.target.value })}>
            <option value="Beginner">Iniciante</option>
            <option value="Intermediate">Intermediário</option>
            <option value="Advanced">Avançado</option>
          </select>
        </div>
        <div>
          <label className={labelClass}>Objetivo</label>
          <select className={inputClass} value={form.goal} onChange={(e) => setForm({ ...form, goal: e.target.value })}>
            <option value="Hypertrophy">Hipertrofia</option>
            <option value="WeightLoss">Emagrecimento</option>
            <option value="Conditioning">Condicionamento</option>
            <option value="Aesthetics">Estética</option>
          </select>
        </div>
        <div>
          <label className={labelClass}>Dias de treino por semana</label>
          <select className={inputClass} value={form.trainingDaysPerWeek}
            onChange={(e) => setForm({ ...form, trainingDaysPerWeek: Number(e.target.value) })}>
            {[2, 3, 4, 5, 6].map((d) => <option key={d} value={d}>{d} dias</option>)}
          </select>
        </div>
      </section>

      <section className="card p-6 space-y-4">
        <div>
          <label className={labelClass}>Grupos musculares priorizados</label>
          <div className="flex flex-wrap gap-2">
            {muscleGroups.map(([value, label]) => (
              <button key={value} type="button"
                onClick={() => setForm({ ...form, priorityMuscleGroups: toggle(form.priorityMuscleGroups, value) })}
                className={`px-3 py-1 rounded-full text-sm border ${
                  form.priorityMuscleGroups.includes(value)
                    ? 'bg-emerald-600 text-white border-emerald-600'
                    : 'border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300'
                }`}>
                {label}
              </button>
            ))}
          </div>
        </div>
        <div>
          <label className={labelClass}>Equipamentos disponíveis (vazio = academia completa)</label>
          <div className="flex flex-wrap gap-2">
            {equipmentOptions.map(([value, label]) => (
              <button key={value} type="button"
                onClick={() => setForm({ ...form, availableEquipment: toggle(form.availableEquipment, value) })}
                className={`px-3 py-1 rounded-full text-sm border ${
                  form.availableEquipment.includes(value)
                    ? 'bg-emerald-600 text-white border-emerald-600'
                    : 'border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300'
                }`}>
                {label}
              </button>
            ))}
          </div>
        </div>
        <div>
          <label className={labelClass}>Lesões ou limitações</label>
          <div className="flex flex-wrap gap-2 mb-2">
            {injuryOptions.map(([value, label]) => (
              <button key={value} type="button"
                onClick={() => setForm({ ...form, injuryTags: toggle(form.injuryTags, value) })}
                className={`px-3 py-1 rounded-full text-sm border ${
                  form.injuryTags.includes(value)
                    ? 'bg-red-600 text-white border-red-600'
                    : 'border-slate-300 dark:border-slate-600 text-slate-600 dark:text-slate-300'
                }`}>
                {label}
              </button>
            ))}
          </div>
          <input className={inputClass} placeholder="Detalhes (opcional)" value={form.injuryNotes}
            onChange={(e) => setForm({ ...form, injuryNotes: e.target.value })} />
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className={labelClass}>Restrições alimentares (separadas por vírgula)</label>
            <input className={inputClass} placeholder="ex.: leite, camarão" value={form.dietaryRestrictions}
              onChange={(e) => setForm({ ...form, dietaryRestrictions: e.target.value })} />
          </div>
          <div>
            <label className={labelClass}>Preferências alimentares</label>
            <input className={inputClass} placeholder="ex.: frango, batata-doce" value={form.foodPreferences}
              onChange={(e) => setForm({ ...form, foodPreferences: e.target.value })} />
          </div>
        </div>
      </section>

      {!hasProfile && (
        <label className="flex items-start gap-3 card p-4 text-sm text-slate-600 dark:text-slate-300">
          <input type="checkbox" checked={consented} onChange={(e) => setConsented(e.target.checked)} className="mt-1" />
          <span>
            Autorizo o tratamento dos meus dados de saúde para geração de treinos e dietas personalizados,
            conforme a LGPD. Os planos gerados são sugestões automatizadas e não substituem avaliação de um
            profissional de saúde.
          </span>
        </label>
      )}

      {error && <p className="text-sm text-red-600">{error}</p>}

      <button type="submit" disabled={saving}
        className="btn-primary px-6 py-2">
        {saving ? 'Salvando…' : hasProfile ? 'Salvar alterações' : 'Concluir onboarding'}
      </button>
    </form>
  )
}
