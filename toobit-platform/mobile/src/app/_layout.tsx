import { useEffect, useState } from 'react'
import { AppState, Platform } from 'react-native'
import { QueryClient, QueryClientProvider, focusManager } from '@tanstack/react-query'
import { Stack } from 'expo-router'
import Constants from 'expo-constants'
import { SafeAreaProvider } from 'react-native-safe-area-context'
import { refreshSession, setBaseUrl } from '../shared/api/client'
import { secure } from '../shared/auth/secureStorage'
import { useAuth } from '../shared/auth/store'
import { bootstrapLanguage, useLangStore } from '../shared/i18n'
import { checkServerUrl } from '../shared/lib/serverUrl'
import { Loading } from '../ui/components'

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: 1, staleTime: 2000 } } })

export default function RootLayout() {
  const [booted, setBooted] = useState(false)
  const langReady = useLangStore((s) => s.ready)
  const token = useAuth((s) => s.accessToken)

  // Pause polling while the app is in the background (saves battery and rate-limit budget).
  useEffect(() => {
    const sub = AppState.addEventListener('change', (st) => { if (Platform.OS !== 'web') focusManager.setFocused(st === 'active') })
    return () => sub.remove()
  }, [])

  useEffect(() => {
    void (async () => {
      await bootstrapLanguage()
      const fallback = (Constants.expoConfig?.extra as { defaultApiBaseUrl?: string } | undefined)?.defaultApiBaseUrl ?? ''
      const saved = await secure.getServer()
      const chk = checkServerUrl(saved ?? fallback)
      if (chk.ok) { setBaseUrl(chk.url); await refreshSession() }
      setBooted(true)
    })()
  }, [])

  // Signing out (or a 401) wipes cached server data so the next user never sees it.
  useEffect(() => { if (!token) queryClient.clear() }, [token])

  if (!booted || !langReady) return <Loading />
  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <Stack screenOptions={{ headerShown: false }}>
          <Stack.Protected guard={!!token}><Stack.Screen name="(tabs)" /></Stack.Protected>
          <Stack.Protected guard={!token}><Stack.Screen name="login" /></Stack.Protected>
        </Stack>
      </QueryClientProvider>
    </SafeAreaProvider>
  )
}
