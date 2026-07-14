const ACCESS_TOKEN_KEY = 'myotrack.accessToken'
const REFRESH_TOKEN_KEY = 'myotrack.refreshToken'

export interface AuthResponse {
  accessToken: string
  refreshToken: string
}

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY)
}

export function storeTokens(tokens: AuthResponse) {
  localStorage.setItem(ACCESS_TOKEN_KEY, tokens.accessToken)
  localStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken)
}

export function clearTokens() {
  localStorage.removeItem(ACCESS_TOKEN_KEY)
  localStorage.removeItem(REFRESH_TOKEN_KEY)
}

export function isAuthenticated(): boolean {
  return getAccessToken() !== null
}

async function refreshTokens(): Promise<boolean> {
  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY)
  if (!refreshToken) return false

  const response = await fetch('/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })
  if (!response.ok) {
    clearTokens()
    return false
  }
  storeTokens((await response.json()) as AuthResponse)
  return true
}

export async function api(path: string, init: RequestInit = {}): Promise<Response> {
  const request = () =>
    fetch(path, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...init.headers,
        ...(getAccessToken() ? { Authorization: `Bearer ${getAccessToken()}` } : {}),
      },
    })

  let response = await request()
  if (response.status === 401 && (await refreshTokens())) {
    response = await request()
  }
  return response
}

/** Variante do api() para multipart/form-data — o browser define o Content-Type com o boundary. */
export async function apiUpload(path: string, body: FormData): Promise<Response> {
  const request = () =>
    fetch(path, {
      method: 'POST',
      body,
      headers: getAccessToken() ? { Authorization: `Bearer ${getAccessToken()}` } : {},
    })

  let response = await request()
  if (response.status === 401 && (await refreshTokens())) {
    response = await request()
  }
  return response
}

export interface JobStatus {
  id: string
  type: string
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
  resultJson: string | null
  lastError: string | null
}

/** Aguarda um job assíncrono terminar (polling a cada 2 s, timeout ~2 min por padrão). */
export async function pollJob(jobId: string, maxAttempts = 60): Promise<JobStatus> {
  for (let i = 0; i < maxAttempts; i++) {
    const response = await api(`/api/jobs/${jobId}`)
    if (!response.ok) throw new Error('Falha ao consultar o status da geração.')
    const job = (await response.json()) as JobStatus
    if (job.status === 'Completed' || job.status === 'Failed') return job
    await new Promise((r) => setTimeout(r, 2000))
  }
  throw new Error('A geração demorou mais do que o esperado. Tente novamente.')
}
