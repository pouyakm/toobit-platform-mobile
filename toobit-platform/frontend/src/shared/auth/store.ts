import { create } from 'zustand'

export type Role = 'Admin' | 'Trader' | 'Viewer'

interface AuthState {
  accessToken: string | null
  accessExpires: number | null // epoch ms
  role: Role | null
  setSession: (s: { accessToken: string; accessExpires: string; refreshToken: string; role: Role }) => void
  clear: () => void
}

// Security trade-off (documented in SECURITY.md): the short-lived access token lives in memory only;
// the single-use rotating refresh token lives in sessionStorage so a page reload does not log the user out.
// No credentials of any kind (exchange keys, secrets) ever reach the browser.
const RT_KEY = 'rt'
export const refreshTokenStore = {
  get: (): string | null => { try { return sessionStorage.getItem(RT_KEY) } catch { return null } },
  set: (v: string) => { try { sessionStorage.setItem(RT_KEY, v) } catch { /* storage unavailable: session ends on reload */ } },
  clear: () => { try { sessionStorage.removeItem(RT_KEY) } catch { /* ignore */ } },
}

export const useAuth = create<AuthState>((set) => ({
  accessToken: null,
  accessExpires: null,
  role: null,
  setSession: (s) => {
    refreshTokenStore.set(s.refreshToken)
    set({ accessToken: s.accessToken, accessExpires: Date.parse(s.accessExpires), role: s.role })
  },
  clear: () => { refreshTokenStore.clear(); set({ accessToken: null, accessExpires: null, role: null }) },
}))

export const canTrade = (role: Role | null) => role === 'Admin' || role === 'Trader'
export const isAdmin = (role: Role | null) => role === 'Admin'
