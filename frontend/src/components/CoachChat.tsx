import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { api, watchJob } from '../lib/api'

interface CoachMessage {
  id: string
  fromUser: boolean
  content: string
  createdAt: string
}

/**
 * Chat flutuante com o coach IA — disponível em todas as telas.
 * A resposta chega via job assíncrono (SSE/polling), como as demais análises.
 */
export default function CoachChat() {
  const location = useLocation()
  const [open, setOpen] = useState(false)
  const [messages, setMessages] = useState<CoachMessage[]>([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const listRef = useRef<HTMLDivElement>(null)

  const load = useCallback(async () => {
    const response = await api('/api/coach/messages')
    if (response.ok) setMessages(await response.json())
  }, [])

  useEffect(() => {
    if (open) load()
  }, [open, load])

  useEffect(() => {
    listRef.current?.scrollTo({ top: listRef.current.scrollHeight })
  }, [messages, sending, open])

  async function send() {
    const content = input.trim()
    if (!content || sending) return
    setError(null)
    setSending(true)
    setInput('')
    // Otimista: mostra a pergunta já; o reload pós-resposta traz os ids reais.
    setMessages((m) => [...m, { id: 'temp', fromUser: true, content, createdAt: new Date().toISOString() }])
    try {
      const response = await api('/api/coach/messages', {
        method: 'POST',
        body: JSON.stringify({ content }),
      })
      if (!response.ok) {
        const data = await response.json().catch(() => null)
        setError(data?.error ?? 'Falha ao enviar a mensagem.')
        setMessages((m) => m.filter((x) => x.id !== 'temp'))
        setInput(content)
        return
      }
      const { jobId } = await response.json()
      const job = await watchJob(jobId)
      if (job.status === 'Failed') setError(job.lastError ?? 'O coach não conseguiu responder.')
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro inesperado.')
    } finally {
      setSending(false)
    }
  }

  // No modo treino o rodapé é do timer de descanso — o chat sairia do caminho.
  if (location.pathname === '/treinar') return null

  if (!open)
    return (
      <button
        onClick={() => setOpen(true)}
        aria-label="Conversar com o coach IA"
        title="Coach IA"
        className="fixed bottom-4 right-4 z-30 flex h-13 w-13 items-center justify-center rounded-full
          bg-linear-to-b from-emerald-500 to-emerald-600 text-2xl shadow-lg shadow-emerald-600/30
          transition-transform hover:scale-105"
      >
        💬
      </button>
    )

  return (
    <div
      className="fixed bottom-4 right-4 z-30 flex h-[32rem] max-h-[75vh] w-[22rem] max-w-[calc(100vw-2rem)]
        flex-col card overflow-hidden"
    >
      <div className="card-header-bg flex items-center justify-between px-4 py-3">
        <span className="font-semibold text-slate-900 dark:text-white">
          Coach <span className="text-emerald-500">IA</span>
        </span>
        <button
          onClick={() => setOpen(false)}
          aria-label="Fechar o chat"
          className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
        >
          ✕
        </button>
      </div>

      <div ref={listRef} className="flex-1 space-y-2 overflow-y-auto p-3">
        {messages.length === 0 && !sending && (
          <p className="p-3 text-sm text-slate-500 dark:text-slate-400">
            Tire dúvidas sobre o seu treino e a sua dieta — o coach conhece os seus planos e a sua
            progressão. Ex.: “posso trocar supino por flexão?”, “o que como no pré-treino?”
          </p>
        )}
        {messages.map((m) => (
          <div key={m.id} className={`flex ${m.fromUser ? 'justify-end' : 'justify-start'}`}>
            <div
              className={`max-w-[85%] whitespace-pre-wrap rounded-2xl px-3 py-2 text-sm ${
                m.fromUser
                  ? 'bg-emerald-600 text-white rounded-br-sm'
                  : 'bg-slate-100 text-slate-800 dark:bg-white/[0.07] dark:text-slate-100 rounded-bl-sm'
              }`}
            >
              {m.content}
            </div>
          </div>
        ))}
        {sending && (
          <div className="flex justify-start">
            <div className="rounded-2xl rounded-bl-sm bg-slate-100 px-3 py-2 text-sm text-slate-500 dark:bg-white/[0.07] dark:text-slate-400">
              O coach está pensando…
            </div>
          </div>
        )}
        {error && <p className="px-1 text-xs text-red-600">{error}</p>}
      </div>

      <div className="border-t border-slate-200/70 p-2 dark:border-white/[0.06]">
        <div className="flex gap-2">
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                send()
              }
            }}
            placeholder="Pergunte ao coach…"
            maxLength={2000}
            disabled={sending}
            className="field flex-1 px-3 py-2 text-sm text-slate-900 dark:text-white"
          />
          <button
            onClick={send}
            disabled={sending || input.trim().length === 0}
            className="btn-primary px-3 py-2 text-sm"
          >
            Enviar
          </button>
        </div>
        <p className="mt-1.5 px-1 text-[10px] leading-tight text-slate-400">
          Orientação geral por IA — não substitui avaliação médica ou de um profissional.
        </p>
      </div>
    </div>
  )
}
