import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import AuthLayout from '../components/AuthLayout'
import PasswordRules from '../components/PasswordRules'

export default function ResetPasswordPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const userId = params.get('uid')
  const token = params.get('token')

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (password !== confirmation) {
      setError('As senhas não conferem.')
      return
    }
    setError(null)
    setLoading(true)
    try {
      const response = await fetch('/api/auth/reset-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId, token, password }),
      })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Não foi possível redefinir a senha.')
        return
      }
      navigate('/login?senha=redefinida', { replace: true })
    } catch {
      setError('Não foi possível redefinir a senha agora. Tente novamente.')
    } finally {
      setLoading(false)
    }
  }

  if (!userId || !token)
    return (
      <AuthLayout subtitle="Link inválido">
        <p className="text-sm text-slate-600 dark:text-slate-300">
          Este link de redefinição está incompleto. Peça um novo na tela de recuperação.
        </p>
        <Link to="/esqueci-a-senha" className="mt-6 block w-full btn-primary py-2.5">
          Pedir novo link
        </Link>
      </AuthLayout>
    )

  return (
    <AuthLayout subtitle="Escolha uma senha nova">
      <form onSubmit={handleSubmit} className="space-y-4">
        <input
          className="w-full field px-3 py-2 text-slate-900 dark:text-white"
          type="password"
          placeholder="Nova senha"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          minLength={8}
          autoFocus
        />

        <PasswordRules password={password} />

        <input
          className="w-full field px-3 py-2 text-slate-900 dark:text-white"
          type="password"
          placeholder="Repita a nova senha"
          value={confirmation}
          onChange={(e) => setConfirmation(e.target.value)}
          required
          minLength={8}
        />

        {error && <p className="text-sm text-red-600">{error}</p>}

        <button type="submit" disabled={loading} className="w-full btn-primary py-2.5">
          {loading ? 'Salvando…' : 'Salvar nova senha'}
        </button>
      </form>

      <p className="mt-4 text-center text-xs text-slate-400">
        Ao redefinir, as sessões abertas em outros aparelhos são encerradas.
      </p>
    </AuthLayout>
  )
}
