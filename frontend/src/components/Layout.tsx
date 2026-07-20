import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { clearTokens, getRoles } from '../lib/api'

const links = [
  { to: '/', label: 'Progresso' },
  { to: '/treino', label: 'Treino' },
  { to: '/treinar', label: 'Treinar' },
  { to: '/dieta', label: 'Dieta' },
  { to: '/registrar', label: 'Registrar' },
  { to: '/refeicoes', label: 'Refeições' },
  { to: '/diario', label: 'Diário' },
  { to: '/videos', label: 'Vídeos' },
  { to: '/assinatura', label: 'Assinatura' },
  { to: '/perfil', label: 'Perfil' },
]

const reviewerRoles = ['Trainer', 'Nutritionist', 'Admin']

export default function Layout() {
  const navigate = useNavigate()
  const isReviewer = getRoles().some((r) => reviewerRoles.includes(r))
  const navLinks = isReviewer ? [...links, { to: '/revisao', label: 'Revisão' }] : links

  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-10 border-b border-slate-200/70 bg-white/70 backdrop-blur-xl dark:border-white/[0.06] dark:bg-[#080c0e]/70">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
          <div className="flex min-w-0 items-center gap-5">
            <span className="flex shrink-0 items-center gap-2">
              <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-linear-to-br from-emerald-400 to-teal-600 text-sm font-bold text-white shadow-md shadow-emerald-600/30">
                M
              </span>
              <span className="font-display text-lg font-bold tracking-tight text-slate-900 dark:text-white">
                Myo<span className="text-emerald-500">Track</span>
              </span>
            </span>
            <nav className="flex gap-0.5 overflow-x-auto">
              {navLinks.map((link) => (
                <NavLink
                  key={link.to}
                  to={link.to}
                  end={link.to === '/'}
                  className={({ isActive }) =>
                    `whitespace-nowrap rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
                      isActive
                        ? 'bg-emerald-500/12 text-emerald-700 dark:bg-emerald-400/10 dark:text-emerald-300'
                        : 'text-slate-500 hover:bg-slate-900/5 hover:text-slate-800 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-slate-100'
                    }`
                  }
                >
                  {link.label}
                </NavLink>
              ))}
            </nav>
          </div>
          <button
            onClick={() => {
              clearTokens()
              navigate('/login')
            }}
            className="shrink-0 rounded-lg px-3 py-1.5 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-900/5 hover:text-slate-800 dark:text-slate-400 dark:hover:bg-white/5 dark:hover:text-slate-100"
          >
            Sair
          </button>
        </div>
      </header>
      <main className="mx-auto max-w-5xl p-4 sm:p-6">
        <Outlet />
      </main>
    </div>
  )
}
