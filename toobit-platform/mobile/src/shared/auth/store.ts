import { create } from 'zustand'
import { secure } from './secureStorage'

export type Role = 'Admin' | 'Trader' | 'Viewer'

export interface Session { accessToken: string; accessExpires: string; refreshToken: string; role: Role }

interface AuthState {
  accessToken: string | null
  accessExpires: number | null // epoch ms
  role: Role | null
  setSession: (s: Session) => Promise<void>
  clear: () => Promise<void>
}

export const useAuth = create<AuthState>((set) => ({
  accessToken: null,
  accessExpires: null,
  role: null,
  setSession: async (s) => {
    set({ accessToken: s.accessToken, accessExpires: Date.parse(s.accessExpires), role: s.role })
    await secure.setRefresh(s.refreshToken) // rotation: every refresh replaces the stored token
  },
  clear: async () => {
    set({ accessToken: null, accessExpires: null, role: null })
    await secure.clearRefresh()
  },
}))

export const canTrade = (role: Role | null): boolean => role === 'Admin' || role === 'Trader'
export const isAdmin = (role: Role | null): boolean => role === 'Admin'
