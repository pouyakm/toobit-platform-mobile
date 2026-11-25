import { useColorScheme } from 'react-native'

// Contrast-checked pairs (WCAG AA >= 4.5:1 for text) for light and dark.
const light = { bg: '#FFFFFF', surface: '#F4F6FA', fg: '#14181F', muted: '#4A5568', line: '#CBD2DC', accent: '#0B5CAD', onAccent: '#FFFFFF', danger: '#B42318', warnBg: '#FFF4D6', warnFg: '#5C3B00', ok: '#116329' }
const dark = { bg: '#10141B', surface: '#1A212C', fg: '#E8ECF2', muted: '#A8B3C4', line: '#2C3646', accent: '#6DB1FF', onAccent: '#0B1220', danger: '#FF8A80', warnBg: '#3A2C00', warnFg: '#FFE29A', ok: '#7EE2A0' }

export type Theme = typeof light
export const useTheme = (): Theme => (useColorScheme() === 'dark' ? dark : light)

export const space = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24 }
export const MIN_TOUCH = 48 // dp: Android accessibility guideline for touch targets
