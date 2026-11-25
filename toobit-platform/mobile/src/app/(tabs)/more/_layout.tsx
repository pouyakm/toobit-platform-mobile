import { Stack } from 'expo-router'
import { useI18n } from '../../../shared/i18n'

export default function MoreLayout() {
  const { t } = useI18n()
  return (
    <Stack>
      <Stack.Screen name="index" options={{ title: t('nav.more') }} />
      <Stack.Screen name="risk" options={{ title: t('nav.risk') }} />
      <Stack.Screen name="settings" options={{ title: t('nav.settings') }} />
    </Stack>
  )
}
