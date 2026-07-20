import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  Bar,
  BarChart,
  CartesianGrid,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { api } from '../lib/api'

interface DiaryEntry {
  id: string
  createdAt: string
  totalKcal: number
  totalProteinG: number
  totalCarbsG: number
  totalFatG: number
  userAdjusted: boolean
  excludedFromDiary: boolean
}

interface Diary {
  date: string
  targets: { kcal: number; proteinG: number; carbsG: number; fatG: number } | null
  consumed: { kcal: number; proteinG: number; carbsG: number; fatG: number }
  entries: DiaryEntry[]
  week: { date: string; kcal: number }[]
}

const axisStyle = { fontSize: 12, fill: 'var(--viz-axis)' }

/** Data local em yyyy-MM-dd (toISOString usaria UTC e erraria o dia à noite). */
function toIsoDate(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function shiftDate(iso: string, days: number) {
  const [y, m, d] = iso.split('-').map(Number)
  return toIsoDate(new Date(y, m - 1, d + days))
}

function formatDay(iso: string) {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString('pt-BR', { weekday: 'long', day: '2-digit', month: '2-digit' })
}

function formatShort(iso: string) {
  const [, month, day] = iso.split('-')
  return `${day}/${month}`
}

/** Medidor de consumo vs. meta. Cor nunca sozinha: o texto traz o percentual. */
function Meter({ label, value, target, unit }: {
  label: string
  value: number
  target: number
  unit: string
}) {
  const pct = target > 0 ? (value / target) * 100 : 0
  const over = pct > 110
  return (
    <div className="card p-4">
      <div className="flex justify-between items-baseline gap-2">
        <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
        <p className={`text-xs whitespace-nowrap ${over ? 'text-amber-600 dark:text-amber-400' : 'text-slate-400'}`}>
          {Math.round(pct)}%{over && ' — acima da meta'}
        </p>
      </div>
      <p className="text-xl font-bold text-slate-900 dark:text-white">
        {Math.round(value)}
        <span className="text-sm font-normal text-slate-400"> / {Math.round(target)} {unit}</span>
      </p>
      <div className="mt-2 h-2 rounded-full bg-slate-200/70 dark:bg-white/10 overflow-hidden">
        <div
          className={`h-full rounded-full transition-all ${over ? 'bg-amber-500' : 'bg-emerald-500'}`}
          style={{ width: `${Math.min(100, pct)}%` }}
        />
      </div>
    </div>
  )
}

export default function DiaryPage() {
  const navigate = useNavigate()
  const today = toIsoDate(new Date())
  const [date, setDate] = useState(today)
  const [diary, setDiary] = useState<Diary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (day: string) => {
    setError(null)
    const response = await api(`/api/diary?date=${day}&tz=${new Date().getTimezoneOffset()}`)
    if (response.ok) setDiary(await response.json())
    else setError('Falha ao carregar o diário.')
    setLoading(false)
  }, [])

  useEffect(() => {
    load(date)
  }, [date, load])

  async function setCounted(entry: DiaryEntry, included: boolean) {
    const response = await api(`/api/diary/entries/${entry.id}`, {
      method: 'PUT',
      body: JSON.stringify({ included }),
    })
    if (response.ok) await load(date)
    else setError('Falha ao atualizar a refeição.')
  }

  if (loading) return <p className="text-slate-500">Carregando…</p>

  const targets = diary?.targets ?? null
  const consumed = diary?.consumed ?? { kcal: 0, proteinG: 0, carbsG: 0, fatG: 0 }
  const entries = diary?.entries ?? []

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <h1 className="page-title">Diário alimentar</h1>
        <div className="flex items-center gap-1.5 text-sm">
          <button
            onClick={() => setDate(shiftDate(date, -1))}
            className="card px-2.5 py-1.5 text-slate-600 dark:text-slate-300 hover:text-emerald-600"
            title="Dia anterior"
          >
            ‹
          </button>
          <span className="card px-3 py-1.5 text-slate-700 dark:text-slate-200 capitalize whitespace-nowrap">
            {formatDay(date)}
          </span>
          <button
            onClick={() => setDate(shiftDate(date, 1))}
            disabled={date >= today}
            className="card px-2.5 py-1.5 text-slate-600 dark:text-slate-300 hover:text-emerald-600 disabled:opacity-40"
            title="Próximo dia"
          >
            ›
          </button>
          {date !== today && (
            <button onClick={() => setDate(today)} className="ml-1 text-emerald-600 hover:underline">
              Hoje
            </button>
          )}
        </div>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {targets ? (
        <section className="grid grid-cols-2 lg:grid-cols-4 gap-3">
          <Meter label="Calorias" value={consumed.kcal} target={targets.kcal} unit="kcal" />
          <Meter label="Proteína" value={consumed.proteinG} target={targets.proteinG} unit="g" />
          <Meter label="Carboidrato" value={consumed.carbsG} target={targets.carbsG} unit="g" />
          <Meter label="Gordura" value={consumed.fatG} target={targets.fatG} unit="g" />
        </section>
      ) : (
        <>
          <section className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            {[
              ['Calorias', consumed.kcal, 'kcal'],
              ['Proteína', consumed.proteinG, 'g'],
              ['Carboidrato', consumed.carbsG, 'g'],
              ['Gordura', consumed.fatG, 'g'],
            ].map(([label, value, unit]) => (
              <div key={label as string} className="card p-4">
                <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
                <p className="text-xl font-bold text-slate-900 dark:text-white">
                  {Math.round(value as number)}
                  <span className="text-sm font-normal text-slate-400"> {unit}</span>
                </p>
              </div>
            ))}
          </section>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            <Link to="/dieta" className="text-emerald-600 hover:underline">Gere sua dieta</Link>{' '}
            para acompanhar o consumo em relação às suas metas diárias.
          </p>
        </>
      )}

      <section className="card overflow-hidden">
        <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg">
          Refeições do dia
        </h2>
        {entries.length === 0 ? (
          <p className="px-5 py-6 text-sm text-slate-500 dark:text-slate-400">
            Nenhuma refeição registrada neste dia.{' '}
            {date === today && (
              <Link to="/refeicoes" className="text-emerald-600 hover:underline">
                Analise a foto de um prato
              </Link>
            )}
          </p>
        ) : (
          <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
            {entries.map((entry) => (
              <li
                key={entry.id}
                className={`px-5 py-2.5 flex items-center justify-between gap-3 ${
                  entry.excludedFromDiary ? 'opacity-50' : ''
                }`}
              >
                <button
                  onClick={() => navigate(`/refeicoes?analise=${entry.id}`)}
                  className="flex-1 flex justify-between items-center gap-3 text-left hover:text-emerald-600"
                >
                  <span className="text-slate-700 dark:text-slate-200">
                    {new Date(entry.createdAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}
                    {entry.userAdjusted && <span className="ml-2 text-xs text-emerald-600">ajustado</span>}
                    {entry.excludedFromDiary && (
                      <span className="ml-2 text-xs text-slate-400">fora do diário</span>
                    )}
                  </span>
                  <span className="text-slate-400 text-xs whitespace-nowrap">
                    {Math.round(entry.totalKcal)} kcal · P {Math.round(entry.totalProteinG)}g · C{' '}
                    {Math.round(entry.totalCarbsG)}g · G {Math.round(entry.totalFatG)}g
                  </span>
                </button>
                <button
                  onClick={() => setCounted(entry, entry.excludedFromDiary)}
                  className="text-xs text-slate-400 hover:text-emerald-600 whitespace-nowrap"
                  title={
                    entry.excludedFromDiary
                      ? 'Voltar a somar esta refeição no dia'
                      : 'Não somar no dia (foto repetida ou prato não consumido)'
                  }
                >
                  {entry.excludedFromDiary ? 'Contar' : 'Não contar'}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {diary && diary.week.some((d) => d.kcal > 0) && (
        <section className="card p-5">
          <h2 className="font-semibold text-slate-900 dark:text-white">Últimos 7 dias</h2>
          <p className="text-xs text-slate-500 dark:text-slate-400 mb-2">
            Calorias consumidas por dia{targets ? ' — linha tracejada marca a meta' : ''}. Clique em um dia para abri-lo.
          </p>
          <div className="h-56">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={diary.week}
                margin={{ top: 8, right: 16, bottom: 0, left: 0 }}
                onClick={(e) => e?.activeLabel && setDate(String(e.activeLabel))}
                className="cursor-pointer"
              >
                <CartesianGrid stroke="var(--viz-grid)" vertical={false} />
                <XAxis dataKey="date" tickFormatter={formatShort} tick={axisStyle} tickLine={false} axisLine={false} />
                <YAxis tick={axisStyle} tickLine={false} axisLine={false} width={64} />
                <Tooltip
                  labelFormatter={(l) => formatDay(String(l))}
                  formatter={(value) => [`${Math.round(Number(value))} kcal`, 'Consumido']}
                  cursor={{ fill: 'var(--viz-grid)', opacity: 0.4 }}
                />
                {targets && (
                  <ReferenceLine
                    y={targets.kcal}
                    stroke="var(--viz-axis)"
                    strokeDasharray="4 4"
                    label={{ value: 'meta', position: 'insideTopRight', fontSize: 11, fill: 'var(--viz-axis)' }}
                  />
                )}
                <Bar dataKey="kcal" name="Consumido" fill="var(--viz-series-2)" radius={[4, 4, 0, 0]} maxBarSize={40} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>
      )}

      <p className="text-xs text-slate-400">
        O diário soma as análises de refeição por foto do dia (estimativas de IA com margem de ±20–30%).
        Use "Não contar" para tirar fotos repetidas ou pratos que você não consumiu.
      </p>
    </div>
  )
}
