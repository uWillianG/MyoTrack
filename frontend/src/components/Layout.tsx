import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { clearTokens } from '../lib/api'

const links = [
  { to: '/', label: 'Progresso' },
  { to: '/treino', label: 'Treino' },
  { to: '/dieta', label: 'Dieta' },
  { to: '/registrar', label: 'Registrar' },
  { to: '/refeicoes', label: 'Refeições' },
  { to: '/videos', label: 'Vídeos' },
  { to: '/perfil', label: 'Perfil' },
]

export default function Layout() {
  const navigate = useNavigate()

  return (
    <div className="min-h-screen bg-slate-100 dark:bg-slate-900">
      <header className="bg-white dark:bg-slate-800 shadow-sm px-4 sm:px-6 py-3 flex items-center justify-between sticky top-0 z-10">
        <div className="flex items-center gap-6">
          <span className="text-lg font-bold text-emerald-600">MyoTrack</span>
          <nav className="flex gap-1 overflow-x-auto">
            {links.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                end={link.to === '/'}
                className={({ isActive }) =>
                  `px-3 py-1.5 rounded-lg text-sm whitespace-nowrap ${
                    isActive
                      ? 'bg-emerald-600 text-white'
                      : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700'
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
          className="text-sm text-slate-500 dark:text-slate-400 hover:underline"
        >
          Sair
        </button>
      </header>
      <main className="max-w-5xl mx-auto p-4 sm:p-6">
        <Outlet />
      </main>
    </div>
  )
}
