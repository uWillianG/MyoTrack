import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { storeTokens, type AuthResponse } from '../lib/api'

export default function LoginPage() {
  const navigate = useNavigate()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

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

  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <form
        onSubmit={handleSubmit}
        className="w-full max-w-sm card p-8 space-y-4"
      >
        <div className="space-y-3 pb-2 text-center">
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-linear-to-br from-emerald-400 to-teal-600 text-xl font-bold text-white shadow-lg shadow-emerald-600/30">
            M
          </span>
          <h1 className="font-display text-3xl font-bold tracking-tight text-slate-900 dark:text-white">
            Myo<span className="text-emerald-500">Track</span>
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            {mode === 'login'
              ? 'Seu personal trainer e nutricionista digital'
              : 'Crie sua conta e comece a evoluir'}
          </p>
        </div>

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
          placeholder="Senha (mín. 8 caracteres)"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          minLength={8}
        />

        {error && <p className="text-sm text-red-600">{error}</p>}

        <button
          type="submit"
          disabled={loading}
          className="w-full btn-primary py-2.5"
        >
          {loading ? 'Aguarde…' : mode === 'login' ? 'Entrar' : 'Cadastrar'}
        </button>

        <button
          type="button"
          onClick={() => setMode(mode === 'login' ? 'register' : 'login')}
          className="w-full text-sm text-emerald-700 dark:text-emerald-400 hover:underline"
        >
          {mode === 'login' ? 'Não tem conta? Cadastre-se' : 'Já tem conta? Entre'}
        </button>
      </form>
    </div>
  )
}
