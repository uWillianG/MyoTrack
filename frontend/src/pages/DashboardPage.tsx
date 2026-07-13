import { useNavigate } from 'react-router-dom'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from 'recharts'
import { clearTokens } from '../lib/api'

// Dados de exemplo — substituídos por SetLogs reais na Fase 1.
const sampleProgress = [
  { week: 'Sem 1', supino: 60, agachamento: 80 },
  { week: 'Sem 2', supino: 62.5, agachamento: 85 },
  { week: 'Sem 3', supino: 62.5, agachamento: 90 },
  { week: 'Sem 4', supino: 65, agachamento: 92.5 },
  { week: 'Sem 5', supino: 67.5, agachamento: 95 },
  { week: 'Sem 6', supino: 70, agachamento: 100 },
]

export default function DashboardPage() {
  const navigate = useNavigate()

  function handleLogout() {
    clearTokens()
    navigate('/login')
  }

  return (
    <div className="min-h-screen bg-slate-100 dark:bg-slate-900">
      <header className="bg-white dark:bg-slate-800 shadow-sm px-6 py-4 flex items-center justify-between">
        <h1 className="text-xl font-bold text-slate-900 dark:text-white">MyoTrack</h1>
        <button
          onClick={handleLogout}
          className="text-sm text-slate-600 dark:text-slate-300 hover:underline"
        >
          Sair
        </button>
      </header>

      <main className="max-w-5xl mx-auto p-6 space-y-6">
        <section className="bg-white dark:bg-slate-800 rounded-xl shadow p-6">
          <h2 className="text-lg font-semibold text-slate-900 dark:text-white mb-1">
            Progressão de carga
          </h2>
          <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
            Dados de exemplo — o registro real de treinos chega na Fase 1.
          </p>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={sampleProgress} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
                <CartesianGrid strokeDasharray="3 3" strokeOpacity={0.3} />
                <XAxis dataKey="week" tickLine={false} />
                <YAxis unit=" kg" tickLine={false} />
                <Tooltip />
                <Line type="monotone" dataKey="supino" name="Supino reto" stroke="#059669" strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey="agachamento" name="Agachamento" stroke="#2563eb" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </section>
      </main>
    </div>
  )
}
