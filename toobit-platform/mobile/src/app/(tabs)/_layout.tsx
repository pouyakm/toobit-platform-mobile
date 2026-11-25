import { Tabs } from 'expo-router/js-tabs'
import { Ionicons } from '@expo/vector-icons'
import type { ColorValue } from 'react-native'
import { useI18n } from '../../shared/i18n'
import { useTheme } from '../../ui/theme'

type IconName = React.ComponentProps<typeof Ionicons>['name']
const icon = (name: IconName) => {
  const TabIcon = ({ color, size }: { color: ColorValue; size: number }) => <Ionicons name={name} color={color as string} size={size} />
  return TabIcon
}

export default function TabsLayout() {
  const { t } = useI18n()
  const c = useTheme()
  return (
    <Tabs screenOptions={{ headerShown: false, tabBarActiveTintColor: c.accent, tabBarInactiveTintColor: c.muted, tabBarStyle: { backgroundColor: c.bg, borderTopColor: c.line }, tabBarLabelStyle: { fontSize: 12 } }}>
      <Tabs.Screen name="index" options={{ title: t('nav.scanner'), tabBarIcon: icon('trending-up') }} />
      <Tabs.Screen name="positions" options={{ title: t('nav.positions'), tabBarIcon: icon('briefcase') }} />
      <Tabs.Screen name="orders" options={{ title: t('nav.orders'), tabBarIcon: icon('list') }} />
      <Tabs.Screen name="signals" options={{ title: t('nav.signals'), tabBarIcon: icon('pulse') }} />
      <Tabs.Screen name="more" options={{ title: t('nav.more'), tabBarIcon: icon('ellipsis-horizontal') }} />
    </Tabs>
  )
}
