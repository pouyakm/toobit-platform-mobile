import { useState } from 'react'
import { KeyboardAvoidingView, Platform, ScrollView, Text } from 'react-native'
import Constants from 'expo-constants'
import { setBaseUrl, api } from '../shared/api/client'
import { secure } from '../shared/auth/secureStorage'
import { useAuth } from '../shared/auth/store'
import { setLanguage, useI18n } from '../shared/i18n'
import { checkServerUrl } from '../shared/lib/serverUrl'
import { Button, Field, Heading, Txt } from '../ui/components'
import { Screen } from '../ui/Screen'
import { space, useTheme } from '../ui/theme'

export default function Login() {
  const { t, lang } = useI18n()
  const c = useTheme()
  const setSession = useAuth((s) => s.setSession)
  const def = (Constants.expoConfig?.extra as { defaultApiBaseUrl?: string } | undefined)?.defaultApiBaseUrl ?? ''
  const [server, setServer] = useState(def)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async () => {
    setErr(null)
    const chk = checkServerUrl(server)
    if (!chk.ok) { setErr(t(chk.error === 'cleartext_public' ? 'login.urlCleartext' : chk.error === 'scheme' ? 'login.urlScheme' : 'login.urlInvalid')); return }
    setBusy(true)
    try {
      setBaseUrl(chk.url)
      const { data } = await api().POST('/api/auth/login', { body: { email: email.trim(), password } })
      if (!data) { setErr(t('login.failed')); return }
      await secure.setServer(chk.url)
      await setSession(data)
    } catch { setErr(t('login.networkError')) } finally { setBusy(false) }
  }

  return (
    <Screen banner={false}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView contentContainerStyle={{ padding: space.lg, gap: space.md }} keyboardShouldPersistTaps="handled">
          <Heading>{t('login.title')}</Heading>
          <Field label={t('login.server')} value={server} onChangeText={setServer} autoCapitalize="none" autoCorrect={false} keyboardType="url" />
          <Txt small muted>{t('login.serverHint')}</Txt>
          <Field label={t('login.email')} value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" autoComplete="username" />
          <Field label={t('login.password')} value={password} onChangeText={setPassword} secureTextEntry autoComplete="current-password" />
          {err && <Text accessibilityRole="alert" accessibilityLiveRegion="assertive" style={{ color: c.danger }}>{err}</Text>}
          <Button title={t('login.submit')} onPress={() => void submit()} disabled={busy || !email || !password} />
          <Button title={lang === 'fa' ? 'English' : 'فارسی'} onPress={() => void setLanguage(lang === 'fa' ? 'en' : 'fa')} />
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  )
}
