export class ApiError extends Error {
  status: number
  details?: Record<string, string[]>

  constructor(message: string, status: number, details?: Record<string, string[]>) {
    super(message)
    this.status = status
    this.details = details
  }
}

let csrfToken: string | null = null

async function getCsrfToken() {
  if (csrfToken) return csrfToken
  const response = await fetch('/api/security/csrf', { credentials: 'include' })
  if (!response.ok) throw new ApiError('Could not initialize request security.', response.status)
  const body = await response.json() as { token: string }
  csrfToken = body.token
  return csrfToken
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const isFormData = init?.body instanceof FormData
  const method = (init?.method ?? 'GET').toUpperCase()
  const isMutation = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  const headers = new Headers(init?.headers)
  if (!isFormData) headers.set('Content-Type', 'application/json')
  if (isMutation) headers.set('X-SukoonDesk-CSRF', await getCsrfToken())
  const response = await fetch(path, {
    ...init,
    credentials: 'include',
    headers,
  })

  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as {
      message?: string
      title?: string
      errors?: Record<string, string[]>
    }
    if (response.status === 401 && !path.startsWith('/api/auth/') && !path.startsWith('/api/public/')) {
      window.dispatchEvent(new Event('sukoondesk:session-expired'))
    }
    throw new ApiError(body.message ?? body.title ?? 'Something went wrong.', response.status, body.errors)
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}
