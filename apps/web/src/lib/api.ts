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
let csrfRequest: Promise<string> | null = null

async function getCsrfToken() {
  if (csrfToken) return csrfToken
  if (!csrfRequest) {
    csrfRequest = (async () => {
      const response = await fetch('/api/security/csrf', { credentials: 'include', cache: 'no-store' })
      if (!response.ok) throw new ApiError('Could not initialize request security.', response.status)
      const body = await response.json() as { token: string }
      csrfToken = body.token
      return body.token
    })()
  }
  try { return await csrfRequest }
  finally { csrfRequest = null }
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const isFormData = init?.body instanceof FormData
  const method = (init?.method ?? 'GET').toUpperCase()
  const isMutation = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  const headers = new Headers(init?.headers)
  if (!isFormData) headers.set('Content-Type', 'application/json')
  if (isMutation) headers.set('X-SukoonDesk-CSRF', await getCsrfToken())
  const send = () => fetch(path, { ...init, credentials: 'include', headers })
  let response = await send()

  if (!response.ok) {
    let body = await response.json().catch(() => ({})) as {
      message?: string
      title?: string
      errors?: Record<string, string[]>
    }
    const errorMessage = body.message ?? body.title ?? ''
    if (isMutation && response.status === 400 && errorMessage.toLowerCase().includes('security token')) {
      csrfToken = null
      headers.set('X-SukoonDesk-CSRF', await getCsrfToken())
      response = await send()
      if (response.ok) body = {}
      else body = await response.json().catch(() => ({})) as typeof body
    }
    if (response.ok) {
      if (path === '/api/auth/sign-in' || path === '/api/auth/sign-out') csrfToken = null
      if (response.status === 204) return undefined as T
      return response.json() as Promise<T>
    }
    if (response.status === 401 && !path.startsWith('/api/auth/') && !path.startsWith('/api/public/')) {
      window.dispatchEvent(new Event('sukoondesk:session-expired'))
    }
    throw new ApiError(body.message ?? body.title ?? 'Something went wrong.', response.status, body.errors)
  }

  if (path === '/api/auth/sign-in' || path === '/api/auth/sign-out') csrfToken = null
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}
