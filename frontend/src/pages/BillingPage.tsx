import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../lib/api'

interface Billing {
  plan: 'Free' | 'Pro'
  maxMealAnalysesPerDay: number
  maxVideoAnalysesPerDay: number
  currentPeriodEnd: string | null
  billingConfigured: boolean
}

export default function BillingPage() {
  const [billing, setBilling] = useState<Billing | null>(null)
  const [loading, setLoading] = useState(true)
  const [redirecting, setRedirecting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [searchParams] = useSearchParams()
  const checkoutStatus = searchParams.get('status')

  useEffect(() => {
    api('/api/billing').then(async (response) => {
      if (response.ok) setBilling(await response.json())
      setLoading(false)
    })
  }, [])

  async function subscribe() {
    setError(null)
    setRedirecting(true)
    try {
      const response = await api('/api/billing/checkout', { method: 'POST' })
      const data = await response.json().catch(() => null)
      if (!response.ok) {
        setError(data?.error ?? 'Falha ao iniciar o checkout.')
        return
      }
      window.location.href = data.url
    } finally {
      setRedirecting(false)
    }
  }

  if (loading) return <p className="text-slate-500">Carregando…</p>

  const isPro = billing?.plan === 'Pro'

  return (
    <div className="space-y-6 max-w-2xl">
      <h1 className="page-title">Assinatura</h1>

      {checkoutStatus === 'sucesso' && (
        <p className="rounded-xl bg-emerald-50 dark:bg-emerald-400/10 text-emerald-700 dark:text-emerald-300 px-4 py-3 text-sm border border-emerald-200/70 dark:border-emerald-400/20">
          Pagamento confirmado! Seu plano Pro será ativado em instantes — recarregue a página se necessário.
        </p>
      )}
      {checkoutStatus === 'cancelado' && (
        <p className="rounded-xl bg-slate-100 dark:bg-white/[0.05] text-slate-600 dark:text-slate-300 px-4 py-3 text-sm border border-slate-200/70 dark:border-white/[0.06]">
          Checkout cancelado. Você continua no plano gratuito.
        </p>
      )}
      {error && <p className="text-sm text-red-600">{error}</p>}

      <section className="card p-6 space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="font-semibold text-slate-900 dark:text-white">Plano atual</h2>
          <span
            className={`rounded-full px-3 py-1 text-sm font-medium ${
              isPro
                ? 'bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300'
                : 'bg-slate-100 dark:bg-slate-700 text-slate-600 dark:text-slate-300'
            }`}
          >
            {isPro ? 'Pro' : 'Gratuito'}
          </span>
        </div>
        <ul className="text-sm text-slate-600 dark:text-slate-300 space-y-1">
          <li>• {billing?.maxMealAnalysesPerDay} análises de refeição por dia</li>
          <li>• {billing?.maxVideoAnalysesPerDay} análises de vídeo por dia</li>
          <li>• Geração de treino e dieta personalizados</li>
        </ul>
        {isPro && billing?.currentPeriodEnd && (
          <p className="text-xs text-slate-400">
            Período atual até {new Date(billing.currentPeriodEnd).toLocaleDateString()}.
          </p>
        )}
      </section>

      {!isPro && (
        <section className="card p-6 space-y-3 border-2 border-emerald-500/40">
          <h2 className="font-semibold text-slate-900 dark:text-white">MyoTrack Pro</h2>
          <ul className="text-sm text-slate-600 dark:text-slate-300 space-y-1">
            <li>• 50 análises de refeição por dia</li>
            <li>• 20 análises de vídeo por dia</li>
            <li>• Prioridade em novos recursos</li>
          </ul>
          {billing?.billingConfigured ? (
            <button
              onClick={subscribe}
              disabled={redirecting}
              className="btn-primary px-4 py-2 text-sm"
            >
              {redirecting ? 'Redirecionando…' : 'Assinar Pro'}
            </button>
          ) : (
            <p className="text-sm text-slate-400">Pagamentos ainda não estão disponíveis neste ambiente.</p>
          )}
        </section>
      )}
    </div>
  )
}
