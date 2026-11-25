import { beforeEach, describe, expect, it } from 'vitest'
import { canTrade, isAdmin, refreshTokenStore, useAuth } from './store'

describe('auth store', () => {
  beforeEach(() => { useAuth.getState().clear(); sessionStorage.clear() })

  it('keeps the access token in memory only and the refresh token in sessionStorage', () => {
    useAuth.getState().setSession({ accessToken: 'AT', accessExpires: new Date(Date.now() + 60000).toISOString(), refreshToken: 'RT', role: 'Trader' })
    expect(useAuth.getState().accessToken).toBe('AT')
    expect(refreshTokenStore.get()).toBe('RT')
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('AT"')
    expect(JSON.stringify({ ...localStorage })).not.toContain('AT')
  })
  it('clear removes everything', () => {
    useAuth.getState().setSession({ accessToken: 'AT', accessExpires: new Date().toISOString(), refreshToken: 'RT', role: 'Admin' })
    useAuth.getState().clear()
    expect(useAuth.getState().accessToken).toBeNull(); expect(refreshTokenStore.get()).toBeNull()
  })
  it('RBAC helpers match the backend roles', () => {
    expect(canTrade('Viewer')).toBe(false); expect(canTrade('Trader')).toBe(true); expect(canTrade('Admin')).toBe(true)
    expect(isAdmin('Trader')).toBe(false); expect(isAdmin('Admin')).toBe(true); expect(canTrade(null)).toBe(false)
  })
})
