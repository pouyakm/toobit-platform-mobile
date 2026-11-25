import { ScrollView } from 'react-native'
import { useRouter } from 'expo-router'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../../shared/auth/store'
import { setLanguage, useI18n } from '../../../shared/i18n'
import { Button, Txt } from '../../../ui/components'
import { Screen } from '../../../ui/Screen'
import { space } from '../../../ui/theme'

export default function More() {
  const { t, lang } = useI18n()
  const router = useRouter()
  const role = useAuth((s) => s.role)
  const clear = useAuth((s) => s.clear)
  const qc = useQueryClient()
  return (
    <Screen banner={false}>
      <ScrollView contentContainerStyle={{ padding: space.lg, gap: space.md }}>
        <Txt muted>{role ?? ''}</Txt>
        <Button title={t('nav.risk')} onPress={() => router.push('/more/risk')} />
        <Button title={t('nav.settings')} onPress={() => router.push('/more/settings')} />
        <Button title={`${t('common.language')}: ${lang === 'fa' ? 'English' : 'فارسی'}`} onPress={() => void setLanguage(lang === 'fa' ? 'en' : 'fa')} />
        <Button danger title={t('common.logout')} onPress={() => { qc.clear(); void clear() }} />
      </ScrollView>
    </Screen>
  )
}
