import { useCallback, useEffect, useRef, useState } from 'react'
import { api, apiUpload, watchJob } from '../lib/api'

interface MealItem {
  description: string
  foodItemId: number | null
  quantityG: number
  kcalPer100g: number
  proteinPer100g: number
  carbsPer100g: number
  fatPer100g: number
}

interface MealAnalysis {
  id: string
  createdAt: string
  userAdjusted: boolean
  totalKcal: number
  totalProteinG: number
  totalCarbsG: number
  totalFatG: number
  photoUrl?: string | null
  mediaExpired?: boolean
  items: MealItem[]
}

interface MealSummary {
  id: string
  createdAt: string
  totalKcal: number
  totalProteinG: number
  totalCarbsG: number
  totalFatG: number
  userAdjusted: boolean
}

function itemMacros(item: MealItem) {
  const factor = item.quantityG / 100
  return {
    kcal: item.kcalPer100g * factor,
    protein: item.proteinPer100g * factor,
    carbs: item.carbsPer100g * factor,
    fat: item.fatPer100g * factor,
  }
}

export default function MealAnalysisPage() {
  const [history, setHistory] = useState<MealSummary[]>([])
  const [analysis, setAnalysis] = useState<MealAnalysis | null>(null)
  // Orientação real da foto (detectada no onLoad) decide o layout:
  // paisagem = banner largo acima dos macros; retrato = coluna ao lado.
  const [photoOrientation, setPhotoOrientation] = useState<'portrait' | 'landscape' | null>(null)
  const [draft, setDraft] = useState<MealItem[] | null>(null)
  const [uploading, setUploading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  const loadHistory = useCallback(async () => {
    const response = await api('/api/meal-analyses')
    if (response.ok) setHistory(await response.json())
  }, [])

  useEffect(() => {
    loadHistory()
  }, [loadHistory])

  async function open(id: string) {
    setError(null)
    setDraft(null)
    setPhotoOrientation(null)
    const response = await api(`/api/meal-analyses/${id}`)
    if (response.ok) setAnalysis(await response.json())
  }

  async function upload(file: File) {
    setError(null)
    setAnalysis(null)
    setDraft(null)
    setPhotoOrientation(null)
    setUploading(true)
    try {
      const form = new FormData()
      form.append('photo', file)
      const response = await apiUpload('/api/meal-analyses', form)
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao enviar a foto.')
        return
      }
      const { jobId } = await response.json()
      const job = await watchJob(jobId)
      if (job.status === 'Failed') {
        setError(job.lastError ?? 'A análise falhou.')
        return
      }
      const result = JSON.parse(job.resultJson ?? '{}') as { mealAnalysisId?: string }
      if (result.mealAnalysisId) await open(result.mealAnalysisId)
      await loadHistory()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro inesperado.')
    } finally {
      setUploading(false)
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  function startEditing() {
    if (analysis) setDraft(analysis.items.map((item) => ({ ...item })))
  }

  function updateQuantity(index: number, quantityG: number) {
    setDraft((items) => items?.map((item, i) => (i === index ? { ...item, quantityG } : item)) ?? null)
  }

  function removeItem(index: number) {
    setDraft((items) => items?.filter((_, i) => i !== index) ?? null)
  }

  async function saveAdjustments() {
    if (!analysis || !draft) return
    if (draft.length === 0) {
      setError('A análise precisa de pelo menos um item.')
      return
    }
    if (draft.some((item) => !(item.quantityG > 0) || item.quantityG > 2000)) {
      setError('Quantidades devem estar entre 1 e 2000 g.')
      return
    }
    setError(null)
    setSaving(true)
    try {
      const response = await api(`/api/meal-analyses/${analysis.id}`, {
        method: 'PUT',
        body: JSON.stringify(draft),
      })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao salvar os ajustes.')
        return
      }
      setAnalysis(await response.json())
      setDraft(null)
      await loadHistory()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro inesperado.')
    } finally {
      setSaving(false)
    }
  }

  const items = draft ?? analysis?.items ?? []
  const totals = draft
    ? draft.reduce(
        (sum, item) => {
          const m = itemMacros(item)
          return { kcal: sum.kcal + m.kcal, protein: sum.protein + m.protein, carbs: sum.carbs + m.carbs, fat: sum.fat + m.fat }
        },
        { kcal: 0, protein: 0, carbs: 0, fat: 0 },
      )
    : analysis
      ? { kcal: analysis.totalKcal, protein: analysis.totalProteinG, carbs: analysis.totalCarbsG, fat: analysis.totalFatG }
      : null

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <h1 className="page-title">Refeições</h1>
        <div>
          <input
            ref={fileInput}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) upload(file)
            }}
          />
          <button
            onClick={() => fileInput.current?.click()}
            disabled={uploading}
            className="btn-primary px-4 py-2 text-sm"
          >
            {uploading ? 'Analisando…' : 'Analisar foto de refeição'}
          </button>
        </div>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {uploading && (
        <div className="card p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Analisando sua refeição com IA… isso leva alguns segundos.</p>
        </div>
      )}

      {analysis && totals && (
        <>
          <section
            className={
              photoOrientation === 'portrait'
                ? 'flex flex-col sm:flex-row gap-3'
                : 'space-y-3'
            }
          >
            {analysis.photoUrl && (
              <a
                href={analysis.photoUrl}
                target="_blank"
                rel="noreferrer"
                title="Abrir foto em tamanho original"
                className={
                  photoOrientation === 'portrait'
                    ? 'card overflow-hidden sm:w-52 shrink-0'
                    : 'card overflow-hidden block'
                }
              >
                <img
                  src={analysis.photoUrl}
                  alt="Foto da refeição analisada"
                  onLoad={(e) =>
                    setPhotoOrientation(
                      e.currentTarget.naturalHeight > e.currentTarget.naturalWidth
                        ? 'portrait'
                        : 'landscape',
                    )
                  }
                  className={
                    photoOrientation === 'portrait'
                      ? 'w-full h-56 sm:h-full sm:max-h-[26rem] object-cover'
                      : 'w-full max-h-72 object-cover'
                  }
                />
              </a>
            )}
            {analysis.mediaExpired && (
              <div
                className={`card flex items-center justify-center p-4 text-xs text-slate-400 text-center ${
                  photoOrientation === 'portrait' ? 'sm:w-52 shrink-0' : ''
                }`}
              >
                Foto removida pela política de retenção de mídia.
              </div>
            )}
            <div
              className={
                photoOrientation === 'portrait'
                  ? 'grid grid-cols-1 gap-3 flex-1 content-start'
                  : 'grid grid-cols-2 sm:grid-cols-4 gap-3'
              }
            >
              {[
                ['Calorias', totals.kcal, 'kcal'],
                ['Proteína', totals.protein, 'g'],
                ['Carboidrato', totals.carbs, 'g'],
                ['Gordura', totals.fat, 'g'],
              ].map(([label, value, unit]) => (
                <div key={label as string} className="card p-4">
                  <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
                  <p className="text-xl font-bold text-slate-900 dark:text-white">
                    {Math.round(value as number)}
                    <span className="text-sm font-normal text-slate-400"> {unit}</span>
                  </p>
                </div>
              ))}
            </div>
          </section>

          <section className="card overflow-hidden">
            <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg flex justify-between items-center">
              <span>
                Itens identificados
                {analysis.userAdjusted && !draft && (
                  <span className="ml-2 text-xs font-normal text-emerald-600">ajustado por você</span>
                )}
              </span>
              {!draft ? (
                <button onClick={startEditing} className="text-sm font-normal text-emerald-600 hover:underline">
                  Ajustar
                </button>
              ) : (
                <span className="flex gap-3">
                  <button onClick={() => setDraft(null)} className="text-sm font-normal text-slate-500 hover:underline">
                    Cancelar
                  </button>
                  <button
                    onClick={saveAdjustments}
                    disabled={saving}
                    className="text-sm font-normal text-emerald-600 hover:underline disabled:opacity-50"
                  >
                    {saving ? 'Salvando…' : 'Salvar ajustes'}
                  </button>
                </span>
              )}
            </h2>
            <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
              {items.map((item, index) => {
                const m = itemMacros(item)
                return (
                  <li key={index} className="px-5 py-2.5 flex items-center justify-between gap-3 text-slate-700 dark:text-slate-200">
                    <span className="flex-1">
                      {item.description}
                      {item.foodItemId == null && (
                        <span className="ml-2 text-xs text-amber-600" title="Item fora do catálogo — macros estimados pela IA">
                          estimado
                        </span>
                      )}
                    </span>
                    {draft ? (
                      <span className="flex items-center gap-2">
                        <input
                          type="number"
                          min={1}
                          max={2000}
                          value={item.quantityG}
                          onChange={(e) => updateQuantity(index, Number(e.target.value))}
                          className="w-20 field px-2 py-1 text-right"
                        />
                        <span className="text-slate-400">g</span>
                        <button
                          onClick={() => removeItem(index)}
                          className="text-red-500 hover:text-red-700 text-xs"
                          title="Remover item"
                        >
                          Remover
                        </button>
                      </span>
                    ) : (
                      <span className="text-slate-400 text-xs whitespace-nowrap">
                        {Math.round(item.quantityG)} g · {Math.round(m.kcal)} kcal · P {Math.round(m.protein)}g · C{' '}
                        {Math.round(m.carbs)}g · G {Math.round(m.fat)}g
                      </span>
                    )}
                  </li>
                )
              })}
            </ul>
          </section>

          <p className="text-xs text-slate-400">
            Estimativa gerada por IA com margem de erro de ±20–30%. Ajuste as quantidades para melhorar a precisão.
            Itens do catálogo usam valores oficiais (base TACO).
          </p>
        </>
      )}

      <section className="card overflow-hidden">
        <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg">
          Histórico
        </h2>
        {history.length === 0 ? (
          <p className="px-5 py-6 text-sm text-slate-500 dark:text-slate-400">
            Nenhuma análise ainda. Envie a foto de um prato para começar.
          </p>
        ) : (
          <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
            {history.map((entry) => (
              <li key={entry.id}>
                <button
                  onClick={() => open(entry.id)}
                  className={`w-full px-5 py-2.5 flex justify-between items-center text-left hover:bg-slate-50 dark:hover:bg-slate-700/30 ${
                    analysis?.id === entry.id ? 'bg-emerald-50 dark:bg-emerald-900/20' : ''
                  }`}
                >
                  <span className="text-slate-700 dark:text-slate-200">
                    {new Date(entry.createdAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}
                    {entry.userAdjusted && <span className="ml-2 text-xs text-emerald-600">ajustado</span>}
                  </span>
                  <span className="text-slate-400 text-xs whitespace-nowrap ml-3">
                    {Math.round(entry.totalKcal)} kcal · P {Math.round(entry.totalProteinG)}g · C{' '}
                    {Math.round(entry.totalCarbsG)}g · G {Math.round(entry.totalFatG)}g
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
