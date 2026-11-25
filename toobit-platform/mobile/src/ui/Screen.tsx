import { type ReactNode } from 'react'
import { View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { HealthBanner } from './components'
import { useTheme } from './theme'

/** Standard tab screen frame: status-bar inset on top, health banner under it. Content is a FlatList/ScrollView from the caller. */
export function Screen({ children, banner = true }: { children: ReactNode; banner?: boolean }) {
  const c = useTheme()
  const insets = useSafeAreaInsets()
  return (
    <View style={{ flex: 1, backgroundColor: c.bg, paddingTop: insets.top }}>
      {banner && <HealthBanner />}
      {children}
    </View>
  )
}
