import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import OnboardingPage from './pages/OnboardingPage'
import WorkoutPlanPage from './pages/WorkoutPlanPage'
import DietPlanPage from './pages/DietPlanPage'
import LogSessionPage from './pages/LogSessionPage'
import MealAnalysisPage from './pages/MealAnalysisPage'
import { isAuthenticated } from './lib/api'

function RequireAuth({ children }: { children: React.ReactNode }) {
  if (!isAuthenticated()) return <Navigate to="/login" replace />
  return children
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          element={
            <RequireAuth>
              <Layout />
            </RequireAuth>
          }
        >
          <Route path="/" element={<DashboardPage />} />
          <Route path="/perfil" element={<OnboardingPage />} />
          <Route path="/treino" element={<WorkoutPlanPage />} />
          <Route path="/dieta" element={<DietPlanPage />} />
          <Route path="/registrar" element={<LogSessionPage />} />
          <Route path="/refeicoes" element={<MealAnalysisPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
