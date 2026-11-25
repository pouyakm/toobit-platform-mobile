import createClient from 'openapi-fetch'
import type { paths } from './schema'
import { refreshTokenStore, useAuth } from '../auth/store'

// Typed client generated from contracts/openapi.json (npm run gen:api). Never hand-edit schema.d.ts.
export const api = createClient<paths>({ baseUrl: '' })

let refreshing: Promise<boolean> | null = null

/** Single-flight rotation: concurrent callers share one refresh so the single-use token is never replayed. */
export function refreshSession(): Promise<boolean> {
  if (refreshing) return refreshing
  const rt = refreshTokenStore.get()
  if (!rt) return Promise.resolve(false)
  refreshing = (async () => {
    const { data } = await createClient<paths>({ baseUrl: '' }).POST('/api/auth/refresh', { body: { refreshToken: rt } })
    if (!data) { useAuth.getState().clear(); return false }
    useAuth.getState().setSession(data)
    return true
  })().finally(() => { refreshing = null })
  return refreshing
}

api.use({
  async onRequest({ request }) {
    const { accessToken, accessExpires } = useAuth.getState()
    if (request.url.includes('/api/auth/')) return request
    if (accessExpires && accessExpires - Date.now() < 30_000) await refreshSession() // refresh shortly BEFORE expiry
    const token = useAuth.getState().accessToken ?? accessToken
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },
  onResponse({ response }) {
    if (response.status === 401 && !response.url.includes('/api/auth/')) useAuth.getState().clear()
    return response
  },
})

/** Unwrap an openapi-fetch result, throwing so TanStack Query can handle errors uniformly. */
export function unwrap<T>(r: { data?: T; error?: unknown; response: Response }): T {
  if (r.data === undefined) throw new Error(`HTTP ${r.response.status}`)
  return r.data
}
