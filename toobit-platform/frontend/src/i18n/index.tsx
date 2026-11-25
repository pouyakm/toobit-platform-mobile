import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { en, type Messages } from './en'
import { fa } from './fa'

export type Lang = 'en' | 'fa'
const bundles: Record<Lang, Messages> = { en, fa }

/** Resolve "a.b.c" against a nested messages object; returns the key itself if missing (visible in dev). */
export function lookup(m: Messages, key: string, vars?: Record<string, string>): string {
  const v = key.split('.').reduce<unknown>((o, k) => (o && typeof o === 'object' ? (o as Record<string, unknown>)[k] : undefined), m)
  if (typeof v !== 'string') return key
  return vars ? v.replace(/\{(\w+)\}/g, (_, n: string) => vars[n] ?? `{${n}}`) : v
}

interface I18n { lang: Lang; setLang: (l: Lang) => void; t: (key: string, vars?: Record<string, string>) => string }
const Ctx = createContext<I18n | null>(null)

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLang] = useState<Lang>(() => { try { return (localStorage.getItem('lang') as Lang) || 'en' } catch { return 'en' } })
  useEffect(() => {
    document.documentElement.lang = lang
    document.documentElement.dir = lang === 'fa' ? 'rtl' : 'ltr' // RTL for Persian
    try { localStorage.setItem('lang', lang) } catch { /* ignore */ }
  }, [lang])
  const value = useMemo<I18n>(() => ({ lang, setLang, t: (k, v) => lookup(bundles[lang], k, v) }), [lang])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useI18n(): I18n {
  const c = useContext(Ctx)
  if (!c) throw new Error('useI18n outside I18nProvider')
  return c
}
