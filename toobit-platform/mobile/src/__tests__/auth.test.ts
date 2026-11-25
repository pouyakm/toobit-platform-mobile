import { useAuth, canTrade, isAdmin } from '../shared/auth/store'
import { secure } from '../shared/auth/secureStorage'
const store: Record<string, string> = {}
jest.mock('expo-secure-store', () => ({
  getItemAsync: jest.fn(async (k: string) => store[k] ?? null),
  setItemAsync: jest.fn(async (k: string, v: string) => { store[k] = v }),
  deleteItemAsync: jest.fn(async (k: string) => { delete store[k] }),
}))

beforeEach(() => { for (const k of Object.keys(store)) delete store[k]; useAuth.setState({ accessToken: null, accessExpires: null, role: null }) })

describe('auth store', () => {
  it('keeps the access token in memory only and the refresh token in secure storage', async () => {
    await useAuth.getState().setSession({ accessToken: 'a', accessExpires: '2030-01-01T00:00:00Z', refreshToken: 'r1', role: 'Trader' })
    expect(useAuth.getState().accessToken).toBe('a')
    expect(await secure.getRefresh()).toBe('r1')
    expect(Object.values(store)).not.toContain('a')
  })
  it('rotates the stored refresh token', async () => {
    const s = { accessToken: 'a', accessExpires: '2030-01-01T00:00:00Z', role: 'Admin' as const }
    await useAuth.getState().setSession({ ...s, refreshToken: 'r1' })
    await useAuth.getState().setSession({ ...s, refreshToken: 'r2' })
    expect(await secure.getRefresh()).toBe('r2')
  })
  it('clear wipes both', async () => {
    await useAuth.getState().setSession({ accessToken: 'a', accessExpires: '2030-01-01T00:00:00Z', refreshToken: 'r1', role: 'Viewer' })
    await useAuth.getState().clear()
    expect(useAuth.getState().accessToken).toBeNull()
    expect(await secure.getRefresh()).toBeNull()
  })
  it('role helpers', () => {
    expect(canTrade('Viewer')).toBe(false); expect(canTrade('Trader')).toBe(true); expect(isAdmin('Trader')).toBe(false); expect(isAdmin('Admin')).toBe(true)
  })
})
