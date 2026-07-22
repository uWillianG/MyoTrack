import { useState } from 'react'
import { Link } from 'react-router-dom'
import AuthLayout from '../components/AuthLayout'

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const response = await fetch('/api/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      })
      if (response.status === 429) {
        setError('Muitas tentativas seguidas. Aguarde alguns minutos e tente de novo.')
        return
      }
      // A resposta é igual exista ou não a conta — não revelamos quem tem cadastro.
      setSent(true)
    } catch {
      setError('Não foi possível enviar agora. Tente novamente.')
    } finally {
      setLoading(false)
    }
  }

  if (sent)
    return (
      <AuthLayout subtitle="Verifique seu e-mail">
        <p className="text-sm text-slate-600 dark:text-slate-300">
          Se houver uma conta com <strong>{email}</strong>, enviamos um link para criar uma senha
          nova. O link vale por 24 horas.
        </p>
        <p className="mt-3 text-sm text-slate-500 dark:text-slate-400">
          Não chegou? Confira a caixa de spam ou{' '}
          <button
            type="button"
            onClick={() => setSent(false)}
            className="text-emerald-700 hover:underline dark:text-emerald-400"
          >
            tente outro e-mail
          </button>
          .
        </p>
        <Link to="/login" className="mt-6 block w-full btn-secondary py-2.5">
          Voltar para o login
        </Link>
      </AuthLayout>
    )

  return (
    <AuthLayout subtitle="Enviamos um link para você criar uma senha nova">
      <form onSubmit={handleSubmit} className="space-y-4">
        <input
          className="w-full field px-3 py-2 text-slate-900 dark:text-white"
          type="email"
          placeholder="E-mail da sua conta"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoFocus
        />

        {error && <p className="text-sm text-red-600">{error}</p>}

        <button type="submit" disabled={loading} className="w-full btn-primary py-2.5">
          {loading ? 'Enviando…' : 'Enviar link de recuperação'}
        </button>
      </form>

      <Link
        to="/login"
        className="mt-4 block text-center text-sm text-emerald-700 hover:underline dark:text-emerald-400"
      >
        Voltar para o login
      </Link>
    </AuthLayout>
  )
}
