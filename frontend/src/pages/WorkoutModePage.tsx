import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../lib/api'

interface PlanExercise {
  id: string
  exerciseId: number
  exerciseName: string
  muscleGroup: string
  tutorialVideoUrl?: string | null
  sets: number
  repsMin: number
  repsMax: number
  restSeconds: number
  notes?: string | null
}

interface PlanDay {
  id: string
  order: number
  label: string
  exercises: PlanExercise[]
}

interface Plan {
  id: string
  name: string
  days: PlanDay[]
}

interface Suggestion {
  workoutDayId: string
  exerciseId: number
  action: 'Start' | 'Increase' | 'ProgressReps' | 'Consolidate'
  nextLoadKg: number | null
  targetReps: number
  incrementKg: number
  lastSessionDate: string | null
  lastSets: { reps: number; loadKg: number }[]
}

interface SessionSummary {
  id: string
  date: string
  workoutDayId: string | null
}

interface SetRow {
  reps: string
  loadKg: string
  done: boolean
}

interface RunExercise extends PlanExercise {
  suggestion: Suggestion | null
  rows: SetRow[]
}

/** Data local em yyyy-MM-dd (toISOString usaria UTC e erraria o dia à noite). */
function toIsoDate(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function formatClock(totalSeconds: number) {
  const m = Math.floor(totalSeconds / 60)
  const s = totalSeconds % 60
  return `${m}:${String(s).padStart(2, '0')}`
}

// O contexto de áudio precisa nascer em um gesto do usuário; criamos no clique
// que inicia o descanso e reaproveitamos para apitar quando o tempo acaba.
let audioCtx: AudioContext | null = null
function ensureAudio() {
  try {
    audioCtx ??= new AudioContext()
    if (audioCtx.state === 'suspended') audioCtx.resume()
  } catch {
    /* áudio indisponível — o timer segue visual */
  }
}
function beep() {
  try {
    if (!audioCtx) return
    const osc = audioCtx.createOscillator()
    const gain = audioCtx.createGain()
    osc.connect(gain)
    gain.connect(audioCtx.destination)
    osc.frequency.value = 880
    gain.gain.setValueAtTime(0.2, audioCtx.currentTime)
    osc.start()
    osc.stop(audioCtx.currentTime + 0.35)
  } catch {
    /* sem som, sem drama */
  }
  navigator.vibrate?.(300)
}

function suggestionText(s: Suggestion | null, ex: PlanExercise) {
  if (!s || s.action === 'Start')
    return s?.nextLoadKg
      ? `Primeira vez: comece com ${s.nextLoadKg} kg.`
      : 'Primeira vez: escolha uma carga confortável para a faixa de repetições.'
  const lastLoad = s.lastSets.length > 0 ? Math.max(...s.lastSets.map((x) => x.loadKg)) : null
  switch (s.action) {
    case 'Increase':
      return `Você fechou as ${ex.repsMax} repetições com ${lastLoad} kg — hoje suba para ${s.nextLoadKg} kg (+${s.incrementKg}).`
    case 'ProgressReps':
      return `Mantenha ${s.nextLoadKg} kg e busque chegar às ${ex.repsMax} repetições em todas as séries.`
    case 'Consolidate':
      return `Consolide ${s.nextLoadKg} kg dentro da faixa antes de subir a carga.`
  }
}

export default function WorkoutModePage() {
  const navigate = useNavigate()
  const [plan, setPlan] = useState<Plan | null>(null)
  const [suggestions, setSuggestions] = useState<Suggestion[]>([])
  const [lastDayId, setLastDayId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [phase, setPhase] = useState<'pick' | 'run' | 'done'>('pick')
  const [exercises, setExercises] = useState<RunExercise[]>([])
  const [dayId, setDayId] = useState<string | null>(null)
  const [exIndex, setExIndex] = useState(0)
  const [rest, setRest] = useState<{ remaining: number; total: number } | null>(null)
  const [startedAt, setStartedAt] = useState<number | null>(null)
  const [elapsed, setElapsed] = useState(0)
  const [saving, setSaving] = useState(false)
  const [summary, setSummary] = useState<{ sets: number; volumeKg: number; duration: number } | null>(null)
  const restRef = useRef(rest)
  restRef.current = rest

  useEffect(() => {
    Promise.all([
      api('/api/workout-plans/active').then((r) => (r.ok ? r.json() : null)),
      api('/api/progress/suggestions').then((r) => (r.ok ? r.json() : [])),
      api('/api/sessions').then((r) => (r.ok ? r.json() : [])),
    ]).then(([p, sug, sessions]: [Plan | null, Suggestion[], SessionSummary[]]) => {
      setPlan(p)
      setSuggestions(sug)
      const last = sessions.find((s) => s.workoutDayId)
      if (last?.workoutDayId) setLastDayId(last.workoutDayId)
      setLoading(false)
    })
  }, [])

  // Relógios: tempo total de treino e contagem regressiva do descanso.
  useEffect(() => {
    if (phase !== 'run') return
    const interval = setInterval(() => {
      if (startedAt) setElapsed(Math.floor((Date.now() - startedAt) / 1000))
      const r = restRef.current
      if (r) {
        if (r.remaining <= 1) {
          beep()
          setRest(null)
        } else {
          setRest({ ...r, remaining: r.remaining - 1 })
        }
      }
    }, 1000)
    return () => clearInterval(interval)
  }, [phase, startedAt])

  // O próximo dia na ordem do split, a partir do último treino registrado.
  const suggestedDayId = useMemo(() => {
    if (!plan || plan.days.length === 0) return null
    const lastIndex = plan.days.findIndex((d) => d.id === lastDayId)
    return plan.days[(lastIndex + 1) % plan.days.length].id
  }, [plan, lastDayId])

  function start(day: PlanDay) {
    const run = day.exercises.map((ex) => {
      const suggestion =
        suggestions.find((s) => s.workoutDayId === day.id && s.exerciseId === ex.exerciseId) ?? null
      const load = suggestion?.nextLoadKg
      const reps = suggestion?.targetReps ?? ex.repsMin
      return {
        ...ex,
        suggestion,
        rows: Array.from({ length: ex.sets }, () => ({
          reps: String(reps),
          loadKg: load != null ? String(load) : '',
          done: false,
        })),
      }
    })
    setExercises(run)
    setDayId(day.id)
    setExIndex(0)
    setRest(null)
    setStartedAt(Date.now())
    setElapsed(0)
    setError(null)
    setPhase('run')
  }

  function updateRow(exIdx: number, rowIdx: number, field: 'reps' | 'loadKg', value: string) {
    setExercises((exs) =>
      exs.map((ex, i) =>
        i === exIdx
          ? { ...ex, rows: ex.rows.map((r, j) => (j === rowIdx ? { ...r, [field]: value } : r)) }
          : ex,
      ),
    )
  }

  function completeSet(exIdx: number, rowIdx: number) {
    ensureAudio()
    const ex = exercises[exIdx]
    const row = ex.rows[rowIdx]
    const reps = Number(row.reps)
    const load = Number(row.loadKg)
    if (!(reps >= 1 && reps <= 100) || !(load >= 0 && load <= 1000)) {
      setError('Preencha repetições (1–100) e carga (0–1000 kg) antes de concluir a série.')
      return
    }
    setError(null)
    setExercises((exs) =>
      exs.map((e, i) =>
        i === exIdx ? { ...e, rows: e.rows.map((r, j) => (j === rowIdx ? { ...r, done: true } : r)) } : e,
      ),
    )
    // Sem descanso depois da última série do último exercício.
    const isLastRow = rowIdx === ex.rows.length - 1
    const isLastExercise = exIdx === exercises.length - 1
    if (!(isLastRow && isLastExercise) && ex.restSeconds > 0)
      setRest({ remaining: ex.restSeconds, total: ex.restSeconds })
  }

  function addRow(exIdx: number) {
    setExercises((exs) =>
      exs.map((ex, i) =>
        i === exIdx
          ? { ...ex, rows: [...ex.rows, { ...ex.rows.at(-1)!, done: false }] }
          : ex,
      ),
    )
  }

  async function finish() {
    const sets = exercises.flatMap((ex) =>
      ex.rows
        .filter((r) => r.done)
        .map((r, idx) => ({
          exerciseId: ex.exerciseId,
          setNumber: idx + 1,
          reps: Number(r.reps),
          loadKg: Number(r.loadKg),
        })),
    )
    if (sets.length === 0) {
      setError('Conclua pelo menos uma série antes de finalizar.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const response = await api('/api/sessions', {
        method: 'POST',
        body: JSON.stringify({ date: toIsoDate(new Date()), workoutDayId: dayId, sets }),
      })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao salvar o treino.')
        return
      }
      setSummary({
        sets: sets.length,
        volumeKg: sets.reduce((sum, s) => sum + s.reps * s.loadKg, 0),
        duration: elapsed,
      })
      setRest(null)
      setPhase('done')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <p className="text-slate-500">Carregando…</p>

  if (!plan)
    return (
      <div className="space-y-6">
        <h1 className="page-title">Treinar</h1>
        <div className="card p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Você ainda não tem um plano de treino ativo.</p>
          <p className="text-sm mt-1">
            <Link to="/treino" className="text-emerald-600 hover:underline">Gere seu plano</Link>{' '}
            para treinar com o modo guiado.
          </p>
        </div>
      </div>
    )

  if (phase === 'pick')
    return (
      <div className="space-y-6">
        <h1 className="page-title">Treinar</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Escolha o dia do seu plano. As cargas já vêm preenchidas com a sugestão de progressão.
        </p>
        <div className="grid gap-3 sm:grid-cols-2">
          {plan.days.map((day) => (
            <button
              key={day.id}
              onClick={() => start(day)}
              className="card p-5 text-left hover:border-emerald-400/60 transition-colors"
            >
              <div className="flex items-center justify-between gap-2">
                <span className="font-semibold text-slate-900 dark:text-white">{day.label}</span>
                {day.id === suggestedDayId && (
                  <span className="text-xs text-emerald-600 bg-emerald-500/10 rounded-full px-2 py-0.5 whitespace-nowrap">
                    sugerido para hoje
                  </span>
                )}
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                {day.exercises.length} exercícios ·{' '}
                {day.exercises.reduce((sum, e) => sum + e.sets, 0)} séries
              </p>
            </button>
          ))}
        </div>
      </div>
    )

  if (phase === 'done' && summary)
    return (
      <div className="space-y-6">
        <h1 className="page-title">Treino concluído 💪</h1>
        <section className="grid grid-cols-3 gap-3">
          {[
            ['Séries', String(summary.sets)],
            ['Volume', `${Math.round(summary.volumeKg).toLocaleString('pt-BR')} kg`],
            ['Duração', formatClock(summary.duration)],
          ].map(([label, value]) => (
            <div key={label} className="card p-4">
              <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
              <p className="text-xl font-bold text-slate-900 dark:text-white">{value}</p>
            </div>
          ))}
        </section>
        <div className="flex gap-3">
          <button onClick={() => navigate('/')} className="btn-primary px-5 py-2 text-sm">
            Ver progresso
          </button>
          <button
            onClick={() => {
              setPhase('pick')
              setSummary(null)
            }}
            className="text-sm text-slate-500 hover:underline"
          >
            Novo treino
          </button>
        </div>
      </div>
    )

  const current = exercises[exIndex]
  const doneCount = exercises.reduce((sum, ex) => sum + ex.rows.filter((r) => r.done).length, 0)
  const totalCount = exercises.reduce((sum, ex) => sum + ex.rows.length, 0)
  const inputClass = 'field px-2 py-1.5 text-slate-900 dark:text-white text-sm text-right'

  return (
    <div className="space-y-4 pb-24">
      <div className="flex items-center justify-between flex-wrap gap-2">
        <h1 className="page-title">{plan.days.find((d) => d.id === dayId)?.label}</h1>
        <div className="flex items-center gap-2 text-sm">
          <span className="card px-3 py-1.5 text-slate-600 dark:text-slate-300 tabular-nums">
            ⏱ {formatClock(elapsed)}
          </span>
          <span className="card px-3 py-1.5 text-slate-600 dark:text-slate-300">
            {doneCount}/{totalCount} séries
          </span>
        </div>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      <section className="card p-5 space-y-4">
        <div>
          <div className="flex items-start justify-between gap-3">
            <h2 className="font-semibold text-lg text-slate-900 dark:text-white">
              {current.exerciseName}
              <span className="ml-2 text-xs font-normal text-slate-400">{current.muscleGroup}</span>
            </h2>
            <span className="text-xs text-slate-400 whitespace-nowrap mt-1">
              {exIndex + 1} de {exercises.length}
            </span>
          </div>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Meta: {current.sets}× {current.repsMin}–{current.repsMax} repetições · descanso{' '}
            {formatClock(current.restSeconds)}
            {current.tutorialVideoUrl && (
              <>
                {' · '}
                <a
                  href={current.tutorialVideoUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="text-emerald-600 hover:underline"
                >
                  ver como fazer
                </a>
              </>
            )}
          </p>
          <p className="mt-1 text-sm text-emerald-700 dark:text-emerald-400">
            {suggestionText(current.suggestion, current)}
          </p>
          {current.notes && <p className="mt-1 text-xs text-slate-400">{current.notes}</p>}
        </div>

        <ul className="space-y-2">
          {current.rows.map((row, rowIdx) => (
            <li
              key={rowIdx}
              className={`flex items-center gap-3 rounded-xl border px-3 py-2 ${
                row.done
                  ? 'border-emerald-300/60 bg-emerald-500/10 dark:border-emerald-400/30'
                  : 'border-slate-200/80 dark:border-white/[0.07]'
              }`}
            >
              <span className="w-8 text-sm text-slate-500 dark:text-slate-400">{rowIdx + 1}ª</span>
              <label className="flex items-center gap-1.5 text-sm text-slate-600 dark:text-slate-300">
                <input
                  type="number"
                  min={1}
                  max={100}
                  value={row.reps}
                  disabled={row.done}
                  onChange={(e) => updateRow(exIndex, rowIdx, 'reps', e.target.value)}
                  className={`${inputClass} w-16`}
                />
                reps
              </label>
              <label className="flex items-center gap-1.5 text-sm text-slate-600 dark:text-slate-300">
                <input
                  type="number"
                  min={0}
                  max={1000}
                  step={0.5}
                  value={row.loadKg}
                  disabled={row.done}
                  onChange={(e) => updateRow(exIndex, rowIdx, 'loadKg', e.target.value)}
                  className={`${inputClass} w-20`}
                />
                kg
              </label>
              <span className="flex-1" />
              {row.done ? (
                <span className="text-emerald-600 text-sm font-medium">✓ feita</span>
              ) : (
                <button
                  onClick={() => completeSet(exIndex, rowIdx)}
                  className="btn-primary px-4 py-1.5 text-sm"
                >
                  Concluir
                </button>
              )}
            </li>
          ))}
        </ul>
        <button
          onClick={() => addRow(exIndex)}
          className="text-sm text-emerald-600 hover:underline"
        >
          + série extra
        </button>
      </section>

      <div className="flex items-center justify-between gap-3">
        <button
          onClick={() => setExIndex((i) => Math.max(0, i - 1))}
          disabled={exIndex === 0}
          className="text-sm text-slate-500 hover:underline disabled:opacity-40"
        >
          ‹ Anterior
        </button>
        {exIndex < exercises.length - 1 ? (
          <button
            onClick={() => setExIndex((i) => i + 1)}
            className="btn-primary px-5 py-2 text-sm"
          >
            Próximo exercício ›
          </button>
        ) : (
          <button onClick={finish} disabled={saving} className="btn-primary px-5 py-2 text-sm">
            {saving ? 'Salvando…' : 'Finalizar treino'}
          </button>
        )}
      </div>

      {/* Exercícios restantes, para se situar no treino. */}
      <ul className="text-sm divide-y divide-slate-100 dark:divide-white/[0.06] card overflow-hidden">
        {exercises.map((ex, i) => (
          <li key={ex.id}>
            <button
              onClick={() => setExIndex(i)}
              className={`w-full px-4 py-2 flex justify-between items-center text-left hover:bg-slate-50 dark:hover:bg-slate-700/30 ${
                i === exIndex ? 'bg-emerald-50 dark:bg-emerald-900/20' : ''
              }`}
            >
              <span className="text-slate-700 dark:text-slate-200">{ex.exerciseName}</span>
              <span className="text-xs text-slate-400">
                {ex.rows.filter((r) => r.done).length}/{ex.rows.length}
              </span>
            </button>
          </li>
        ))}
      </ul>

      {/* Timer de descanso fixo no rodapé — visível enquanto o usuário rola a tela. */}
      {rest && (
        <div className="fixed inset-x-0 bottom-0 z-20 border-t border-emerald-500/20 bg-white/90 backdrop-blur-xl dark:bg-[#080c0e]/90">
          <div className="mx-auto max-w-5xl px-4 py-3 sm:px-6 flex items-center gap-4">
            <span className="text-2xl font-bold tabular-nums text-slate-900 dark:text-white">
              {formatClock(rest.remaining)}
            </span>
            <div className="flex-1 h-2 rounded-full bg-slate-200/70 dark:bg-white/10 overflow-hidden">
              <div
                className="h-full rounded-full bg-emerald-500 transition-all"
                style={{ width: `${(rest.remaining / rest.total) * 100}%` }}
              />
            </div>
            <button
              onClick={() => setRest({ ...rest, remaining: rest.remaining + 30, total: rest.total + 30 })}
              className="text-sm text-slate-500 hover:underline whitespace-nowrap"
            >
              +30s
            </button>
            <button
              onClick={() => setRest(null)}
              className="text-sm text-emerald-600 hover:underline whitespace-nowrap"
            >
              Pular descanso
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
