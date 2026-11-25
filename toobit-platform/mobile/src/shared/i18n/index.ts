import { I18nManager } from 'react-native'
import { create } from 'zustand'
import { secure } from '../auth/secureStorage'
import { en, type Messages } from './en'
import { fa } from './fa'

export type Lang = 'en' | 'fa'
const bundles: Record<Lang, Messages> = { en, fa }
export const isRtl = (l: Lang): boolean => l === 'fa'

/** Resolve "a.b.c" against a nested messages object; returns the key itself if missing (visible in dev). */
export function lookup(m: Messages, key: string, vars?: Record<string, string>): string {
  const v = key.split('.').reduce<unknown>((o, k) => (o && typeof o === 'object' ? (o as Record<string, unknown>)[k] : undefined), m)
  if (typeof v !== 'string') return key
  return vars ? v.replace(/\{(\w+)\}/g, (_, n: string) => vars[n] ?? `{${n}}`) : v
}

interface I18nState { lang: Lang; ready: boolean }
export const useLangStore = create<I18nState>(() => ({ lang: 'en', ready: false }))

/**
 * RTL on Android is a process-level layout flag: changing it requires a reload.
 * Returns true when the native direction already matches (safe to render), false when a reload was triggered.
 */
export async function ensureDirection(lang: Lang): Promise<boolean> {
  const want = isRtl(lang)
  if (I18nManager.isRTL === want) return true
  I18nManager.allowRTL(want)
  I18nManager.forceRTL(want)
  try {
    const Updates = await import('expo-updates')
    await Updates.reloadAsync()
  } catch {
    try {
      const { DevSettings } = await import('react-native')
      DevSettings.reload() // development client fallback (expo-updates reload is unavailable in dev)
    } catch { /* nothing else to try: the new direction applies on the next manual restart */ }
  }
  return false
}

/** Called once at startup: restores the saved language and makes sure the layout direction matches it. */
export async function bootstrapLanguage(): Promise<void> {
  const saved = await secure.getLang()
  const lang: Lang = saved === 'fa' ? 'fa' : 'en'
  useLangStore.setState({ lang })
  const ok = await ensureDirection(lang)
  if (ok) useLangStore.setState({ ready: true })
}

export async function setLanguage(lang: Lang): Promise<void> {
  await secure.setLang(lang)
  useLangStore.setState({ lang })
  await ensureDirection(lang) // reloads the app if the direction changes
}

export function useI18n() {
  const lang = useLangStore((s) => s.lang)
  return { lang, rtl: isRtl(lang), t: (key: string, vars?: Record<string, string>) => lookup(bundles[lang], key, vars) }
}
