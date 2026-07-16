import { useCallback, useEffect, useRef, useState } from 'react'
import { api, watchJob } from '../lib/api'

interface VideoIssue {
  code: string
  message: string
  timestamps_sec: number[]
}

interface CorrectPoint {
  code: string
  message: string
}

interface VideoAnalysis {
  id: string
  createdAt: string
  analyzedExercise: string
  score: number | null
  repCount: number
  playbackUrl: string
  hasOverlay: boolean
  result: {
    issues: VideoIssue[]
    // Análises antigas (antes do feedback positivo) não têm o campo.
    correctPoints?: CorrectPoint[]
    metrics: Record<string, number | null>
    notEvaluableReason: string | null
  }
}

interface VideoSummary {
  id: string
  createdAt: string
  analyzedExercise: string
  score: number | null
  repCount: number
}

const exerciseLabels: Record<string, string> = {
  squat: 'Agachamento',
  lunge: 'Afundo (passada)',
  deadlift: 'Levantamento terra',
  romanian_deadlift: 'Terra romeno (stiff)',
  hip_thrust: 'Elevação de quadril (hip thrust)',
  bench_press: 'Supino',
  push_up: 'Flexão de braço',
  overhead_press: 'Desenvolvimento (militar)',
  barbell_row: 'Remada curvada',
  lat_pulldown: 'Puxada alta (pulldown)',
  seated_cable_row: 'Remada baixa sentada',
  dumbbell_row: 'Remada serrote (unilateral)',
  biceps_curl: 'Rosca bíceps',
  hammer_curl: 'Rosca martelo',
  preacher_curl: 'Rosca Scott',
  pull_up: 'Barra fixa',
  lateral_raise: 'Elevação lateral',
}

const MAX_VIDEO_BYTES = 100 * 1024 * 1024

function formatTime(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}:${s.toString().padStart(2, '0')}`
}

export default function VideoAnalysisPage() {
  const [exercise, setExercise] = useState('squat')
  const [history, setHistory] = useState<VideoSummary[]>([])
  const [analysis, setAnalysis] = useState<VideoAnalysis | null>(null)
  const [phase, setPhase] = useState<'idle' | 'uploading' | 'analyzing'>('idle')
  const [error, setError] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const player = useRef<HTMLVideoElement>(null)

  const loadHistory = useCallback(async () => {
    const response = await api('/api/video-analyses')
    if (response.ok) setHistory(await response.json())
  }, [])

  useEffect(() => {
    loadHistory()
  }, [loadHistory])

  async function open(id: string) {
    setError(null)
    const response = await api(`/api/video-analyses/${id}`)
    if (response.ok) setAnalysis(await response.json())
  }

  async function upload(file: File) {
    setError(null)
    setAnalysis(null)
    if (file.size > MAX_VIDEO_BYTES) {
      setError('Envie um vídeo de até 100 MB (no máximo 60 segundos).')
      return
    }
    setPhase('uploading')
    try {
      const contentType = file.type || 'video/mp4'
      const presignResponse = await api('/api/video-analyses/presign', {
        method: 'POST',
        body: JSON.stringify({ contentType }),
      })
      if (!presignResponse.ok) {
        const data = await presignResponse.json().catch(() => null)
        setError(data?.error ?? 'Falha ao preparar o upload.')
        return
      }
      const { mediaKey, uploadUrl } = await presignResponse.json()

      // Upload direto no storage — o vídeo não passa pela API.
      const putResponse = await fetch(uploadUrl, {
        method: 'PUT',
        body: file,
        headers: { 'Content-Type': contentType },
      })
      if (!putResponse.ok) {
        setError('Falha ao enviar o vídeo para o storage.')
        return
      }

      const createResponse = await api('/api/video-analyses', {
        method: 'POST',
        body: JSON.stringify({ mediaKey, exercise }),
      })
      if (!createResponse.ok) {
        const data = await createResponse.json().catch(() => null)
        setError(data?.error ?? 'Falha ao iniciar a análise.')
        return
      }

      setPhase('analyzing')
      const { jobId } = await createResponse.json()
      // Análise de vídeo é lenta (pose frame a frame) — até ~10 min de polling.
      const job = await watchJob(jobId, 300)
      if (job.status === 'Failed') {
        setError(job.lastError ?? 'A análise falhou.')
        return
      }
      const result = JSON.parse(job.resultJson ?? '{}') as { videoAnalysisId?: string }
      if (result.videoAnalysisId) await open(result.videoAnalysisId)
      await loadHistory()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro inesperado.')
    } finally {
      setPhase('idle')
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  function seekTo(seconds: number) {
    if (player.current) {
      player.current.currentTime = seconds
      player.current.play()
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <h1 className="page-title">Análise de vídeo</h1>
        <div className="flex items-center gap-2">
          <select
            value={exercise}
            onChange={(e) => setExercise(e.target.value)}
            className="field px-3 py-2 text-sm text-slate-900 dark:text-white"
          >
            {Object.entries(exerciseLabels).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
          <input
            ref={fileInput}
            type="file"
            accept="video/mp4,video/quicktime,video/webm"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) upload(file)
            }}
          />
          <button
            onClick={() => fileInput.current?.click()}
            disabled={phase !== 'idle'}
            className="btn-primary px-4 py-2 text-sm"
          >
            {phase === 'uploading' ? 'Enviando…' : phase === 'analyzing' ? 'Analisando…' : 'Enviar vídeo'}
          </button>
        </div>
      </div>

      <p className="text-xs text-slate-400">
        Grave de lado (câmera lateral), com o corpo inteiro no enquadramento e boa iluminação —
        na vertical ou na horizontal, como preferir. Vídeos de até 60 segundos e 100 MB — apenas
        a série, sem preparação.
      </p>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {phase === 'analyzing' && (
        <div className="card p-8 text-center text-slate-500 dark:text-slate-400">
          <p>Analisando o movimento frame a frame… isso pode levar alguns minutos.</p>
        </div>
      )}

      {analysis && (
        <>
          <section className="grid grid-cols-3 gap-3">
            <div className="card p-4">
              <p className="text-xs text-slate-500 dark:text-slate-400">Exercício</p>
              <p className="text-lg font-bold text-slate-900 dark:text-white">
                {exerciseLabels[analysis.analyzedExercise] ?? analysis.analyzedExercise}
              </p>
            </div>
            <div className="card p-4">
              <p className="text-xs text-slate-500 dark:text-slate-400">Score</p>
              <p className="text-lg font-bold text-slate-900 dark:text-white">
                {analysis.score !== null ? `${analysis.score}/100` : '—'}
              </p>
            </div>
            <div className="card p-4">
              <p className="text-xs text-slate-500 dark:text-slate-400">Repetições</p>
              <p className="text-lg font-bold text-slate-900 dark:text-white">{analysis.repCount}</p>
            </div>
          </section>

          <section className="card overflow-hidden">
            <video ref={player} src={analysis.playbackUrl} controls className="w-full max-h-[28rem] bg-black" />
            {analysis.hasOverlay && (
              <p className="px-5 py-2 text-xs text-slate-400">
                Vídeo com o esqueleto detectado — confira se os pontos acompanham o corpo; se não acompanham,
                a análise não é confiável.
              </p>
            )}
          </section>

          {analysis.result.notEvaluableReason ? (
            <div className="bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-xl p-4 text-sm text-amber-800 dark:text-amber-200">
              <p className="font-semibold">Não foi possível avaliar com confiança</p>
              <p>{analysis.result.notEvaluableReason}</p>
            </div>
          ) : (
            <>
              {(analysis.result.correctPoints ?? []).length > 0 && (
                <section className="card overflow-hidden">
                  <h2 className="px-5 py-3 font-semibold text-emerald-700 dark:text-emerald-300 card-header-bg">
                    Pontos corretos
                  </h2>
                  <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
                    {(analysis.result.correctPoints ?? []).map((point) => (
                      <li key={point.code} className="px-5 py-3 flex gap-2 text-slate-700 dark:text-slate-200">
                        <span className="text-emerald-500 shrink-0">✓</span>
                        <span>{point.message}</span>
                      </li>
                    ))}
                  </ul>
                </section>
              )}

              {analysis.result.issues.length === 0 ? (
                <div className="bg-emerald-50 dark:bg-emerald-900/20 border border-emerald-200 dark:border-emerald-800 rounded-xl p-4 text-sm text-emerald-800 dark:text-emerald-200">
                  Nenhum erro detectado pelas heurísticas. Boa execução!
                </div>
              ) : (
                <section className="card overflow-hidden">
                  <h2 className="px-5 py-3 font-semibold text-amber-700 dark:text-amber-300 card-header-bg">
                    Pontos de atenção
                  </h2>
                  <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
                    {analysis.result.issues.map((issue) => (
                      <li key={issue.code} className="px-5 py-3 text-slate-700 dark:text-slate-200">
                        <p className="flex gap-2">
                          <span className="text-amber-500 shrink-0">⚠</span>
                          <span>{issue.message}</span>
                        </p>
                        <p className="mt-1 flex flex-wrap gap-1.5 pl-6">
                          {issue.timestamps_sec.map((t, i) => (
                            <button
                              key={i}
                              onClick={() => seekTo(t)}
                              className="rounded-lg bg-slate-100 dark:bg-white/[0.06] hover:bg-emerald-100 dark:hover:bg-emerald-900/40 px-2 py-0.5 text-xs text-slate-600 dark:text-slate-300"
                            >
                              ▶ {formatTime(t)}
                            </button>
                          ))}
                        </p>
                      </li>
                    ))}
                  </ul>
                </section>
              )}
            </>
          )}

          <p className="text-xs text-slate-400">
            Análise automática baseada em estimativa de pose — sensível ao ângulo da câmera e à iluminação.
            Use como apoio, não como substituto da orientação de um profissional.
          </p>
        </>
      )}

      <section className="card overflow-hidden">
        <h2 className="px-5 py-3 font-semibold text-slate-900 dark:text-white card-header-bg">
          Histórico
        </h2>
        {history.length === 0 ? (
          <p className="px-5 py-6 text-sm text-slate-500 dark:text-slate-400">
            Nenhuma análise ainda. Escolha o exercício, envie um vídeo da série e receba os pontos
            corretos e os pontos de atenção da execução.
          </p>
        ) : (
          <ul className="divide-y divide-slate-100 dark:divide-white/[0.06] text-sm">
            {history.map((entry) => (
              <li key={entry.id}>
                <button
                  onClick={() => open(entry.id)}
                  className={`w-full px-5 py-2.5 flex justify-between items-center text-left hover:bg-slate-50 dark:hover:bg-slate-700/30 ${
                    analysis?.id === entry.id ? 'bg-emerald-50 dark:bg-emerald-900/20' : ''
                  }`}
                >
                  <span className="text-slate-700 dark:text-slate-200">
                    {new Date(entry.createdAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}
                    <span className="ml-2 text-slate-500">
                      {exerciseLabels[entry.analyzedExercise] ?? entry.analyzedExercise}
                    </span>
                  </span>
                  <span className="text-slate-400 text-xs whitespace-nowrap ml-3">
                    {entry.repCount} reps · {entry.score !== null ? `${entry.score}/100` : 'não avaliado'}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
