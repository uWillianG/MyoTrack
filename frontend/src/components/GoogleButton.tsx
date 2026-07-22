import { useEffect, useState } from 'react'

/**
 * Só aparece quando o servidor tem as credenciais do OAuth configuradas —
 * um botão que leva a um erro é pior do que botão nenhum.
 */
export default function GoogleButton({ label }: { label: string }) {
  const [enabled, setEnabled] = useState(false)

  useEffect(() => {
    fetch('/api/auth/providers')
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => setEnabled(Boolean(data?.google)))
      .catch(() => setEnabled(false))
  }, [])

  if (!enabled) return null

  return (
    <>
      <div className="my-4 flex items-center gap-3">
        <span className="h-px flex-1 bg-slate-200 dark:bg-white/10" />
        <span className="text-xs text-slate-400">ou</span>
        <span className="h-px flex-1 bg-slate-200 dark:bg-white/10" />
      </div>
      <a href="/api/auth/google/start" className="w-full btn-secondary py-2.5">
        <svg viewBox="0 0 18 18" aria-hidden="true" className="h-4.5 w-4.5">
          <path fill="#4285F4" d="M17.64 9.2c0-.64-.06-1.25-.16-1.84H9v3.48h4.84a4.14 4.14 0 0 1-1.8 2.72v2.26h2.92c1.7-1.57 2.68-3.88 2.68-6.62Z" />
          <path fill="#34A853" d="M9 18c2.43 0 4.47-.8 5.96-2.18l-2.92-2.26c-.81.54-1.84.86-3.04.86-2.34 0-4.32-1.58-5.03-3.7H.96v2.33A9 9 0 0 0 9 18Z" />
          <path fill="#FBBC05" d="M3.97 10.72a5.4 5.4 0 0 1 0-3.44V4.95H.96a9 9 0 0 0 0 8.1l3.01-2.33Z" />
          <path fill="#EA4335" d="M9 3.58c1.32 0 2.5.45 3.44 1.35l2.58-2.58C13.46.89 11.43 0 9 0A9 9 0 0 0 .96 4.95l3.01 2.33C4.68 5.16 6.66 3.58 9 3.58Z" />
        </svg>
        {label}
      </a>
    </>
  )
}
