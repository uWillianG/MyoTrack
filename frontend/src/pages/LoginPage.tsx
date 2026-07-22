import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import AuthLayout from '../components/AuthLayout'
import GoogleButton from '../components/GoogleButton'
import PasswordRules from '../components/PasswordRules'
import { storeTokens, type AuthResponse } from '../lib/api'

/** Mensagens dos erros que o callback do OAuth devolve na querystring. */
const oauthErrors: Record<string, string> = {
  'google-indisponivel': 'Login com Google indisponível no momento.',
  'google-cancelado': 'Login com Google cancelado.',
  'google-state': 'Falha na verificação de segurança do login. Tente novamente.',
  'google-email': 'Sua conta Google não tem um e-mail verificado.',
  'google-falhou': 'Não foi possível entrar com o Google. Tente novamente.',
}

export default function LoginPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const oauthCode = params.get('oauth')
  const oauthError = params.get('erro')
  const passwordReset = params.get('senha') === 'redefinida'

  // Volta do Google: troca o código de uso único pelo par de tokens.
  useEffect(() => {
    if (oauthError) {
      setError(oauthErrors[oauthError] ?? 'Não foi possível entrar. Tente novamente.')
      setParams({}, { replace: true })
      return
    }
    if (!oauthCode) return

    setLoading(true)
    fetch('/api/auth/google/exchange', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code: oauthCode }),
    })
      .then(async (response) => {
        if (!response.ok) {
          setError('Sessão do Google expirada. Tente entrar novamente.')
          setParams({}, { replace: true })
          return
        }
        storeTokens((await response.json()) as AuthResponse)
        navigate('/', { replace: true })
      })
      .catch(() => setError('Não foi possível concluir o login com o Google.'))
      .finally(() => setLoading(false))
  }, [oauthCode, oauthError, navigate, setParams])

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const body =
        mode === 'login' ? { email, password } : { email, password, displayName }
      const response = await fetch(`/api/auth/${mode}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(
          data?.error ?? data?.errors?.join(' ') ?? 'Falha na autenticação. Tente novamente.',
        )
        return
      }
      storeTokens((await response.json()) as AuthResponse)
      navigate(mode === 'register' ? '/perfil' : '/')
    } finally {
      setLoading(false)
    }
  }

  if (oauthCode && !error)
    return (
      <AuthLayout subtitle="Entrando com o Google…">
        <p className="text-center text-sm text-slate-500 dark:text-slate-400">Só um instante…</p>
      </AuthLayout>
    )

  return (
    <AuthLayout
      subtitle={
        mode === 'login'
          ? 'Seu personal trainer e nutricionista digital'
          : 'Crie sua conta e comece a evoluir'
      }
    >
      {passwordReset && (
        <p className="mb-4 rounded-xl bg-emerald-500/10 px-3 py-2 text-sm text-emerald-700 dark:text-emerald-400">
          Senha redefinida. Entre com a nova senha.
        </p>
      )}

      <form onSubmit={handleSubmit} className="space-y-4">
        {mode === 'register' && (
          <input
            className="w-full field px-3 py-2 text-slate-900 dark:text-white"
            placeholder="Nome"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />
        )}
        <input
          className="w-full field px-3 py-2 text-slate-900 dark:text-white"
          type="email"
          placeholder="E-mail"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <input
          className="w-full field px-3 py-2 text-slate-900 dark:text-white"
          type="password"
          placeholder="Senha"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          minLength={8}
        />

        {mode === 'register' && <PasswordRules password={password} />}

        {error && <p className="text-sm text-red-600">{error}</p>}

        <button type="submit" disabled={loading} className="w-full btn-primary py-2.5">
          {loading ? 'Aguarde…' : mode === 'login' ? 'Entrar' : 'Cadastrar'}
        </button>
      </form>

      {mode === 'login' && (
        <p className="mt-3 text-center">
          <Link
            to="/esqueci-a-senha"
            className="text-sm text-slate-500 hover:text-emerald-700 hover:underline dark:text-slate-400 dark:hover:text-emerald-400"
          >
            Esqueci minha senha
          </Link>
        </p>
      )}

      <GoogleButton label={mode === 'login' ? 'Entrar com Google' : 'Cadastrar com Google'} />

      <button
        type="button"
        onClick={() => {
          setMode(mode === 'login' ? 'register' : 'login')
          setError(null)
        }}
        className="mt-4 w-full text-sm text-emerald-700 dark:text-emerald-400 hover:underline"
      >
        {mode === 'login' ? 'Não tem conta? Cadastre-se' : 'Já tem conta? Entre'}
      </button>
    </AuthLayout>
  )
}
