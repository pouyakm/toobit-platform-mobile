import * as SecureStore from 'expo-secure-store'

// Android Keystore-backed. Only the single-use ROTATING refresh token and non-secret prefs are stored.
// The 15-minute access token lives in memory only. No exchange credentials ever reach the device.
const REFRESH_KEY = 'toobit.refresh'
const SERVER_KEY = 'toobit.server'
const LANG_KEY = 'toobit.lang'

async function safeGet(key: string): Promise<string | null> {
  try { return await SecureStore.getItemAsync(key) } catch { return null }
}
async function safeSet(key: string, value: string): Promise<void> {
  try { await SecureStore.setItemAsync(key, value) } catch { /* storage unavailable: the session just won't survive a restart */ }
}
async function safeDelete(key: string): Promise<void> {
  try { await SecureStore.deleteItemAsync(key) } catch { /* ignore */ }
}

export const secure = {
  getRefresh: () => safeGet(REFRESH_KEY),
  setRefresh: (v: string) => safeSet(REFRESH_KEY, v),
  clearRefresh: () => safeDelete(REFRESH_KEY),
  getServer: () => safeGet(SERVER_KEY),
  setServer: (v: string) => safeSet(SERVER_KEY, v),
  getLang: () => safeGet(LANG_KEY),
  setLang: (v: string) => safeSet(LANG_KEY, v),
}
