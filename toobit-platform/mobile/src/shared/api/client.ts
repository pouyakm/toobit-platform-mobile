import createClient from 'openapi-fetch'
import type { paths } from './schema'
import { secure } from '../auth/secureStorage'
import { useAuth } from '../auth/store'

// Typed client generated from ../contracts/openapi.json (npm run gen:api). Never hand-edit schema.d.ts.
// The base URL is user-configurable (login screen), so clients are built per URL and memoized.
let baseUrl = ''
const clients = new Map<string, ReturnType<typeof build>>()

function build(url: string) {
  const c = createClient<paths>({ baseUrl: url })
  c.use({
    async onRequest({ request }) {
      if (request.url.includes('/api/auth/')) return request
      const { accessExpires } = useAuth.getState()
      if (accessExpires && accessExpires - Date.now() < 30_000) await refreshSession() // refresh shortly BEFORE expiry
      const token = useAuth.getState().accessToken
      if (token) request.headers.set('Authorization', `Bearer ${token}`)
      return request
    },
    onResponse({ response }) {
      if (response.status === 401 && !response.url.includes('/api/auth/')) void useAuth.getState().clear()
      return response
    },
  })
  return c
}

export function setBaseUrl(url: string): void { baseUrl = url }
export function getBaseUrl(): string { return baseUrl }
export function api() {
  let c = clients.get(baseUrl)
  if (!c) { c = build(baseUrl); clients.set(baseUrl, c) }
  return c
}

let refreshing: Promise<boolean> | null = null

/** Single-flight rotation: concurrent callers share one refresh so the single-use token is never replayed. */
export function refreshSession(): Promise<boolean> {
  if (refreshing) return refreshing
  refreshing = (async () => {
    const rt = await secure.getRefresh()
    if (!rt) return false
    try {
      const { data } = await createClient<paths>({ baseUrl }).POST('/api/auth/refresh', { body: { refreshToken: rt } })
      if (!data) { await useAuth.getState().clear(); return false }
      await useAuth.getState().setSession(data)
      return true
    } catch {
      return false // network error: keep the stored token, try again later (don't log the user out for being offline)
    }
  })().finally(() => { refreshing = null })
  return refreshing
}

/** Unwrap an openapi-fetch result, throwing so TanStack Query can handle errors uniformly. */
export function unwrap<T>(r: { data?: T; error?: unknown; response: Response }): T {
  if (r.data === undefined) throw new Error(`HTTP ${r.response.status}`)
  return r.data
}
