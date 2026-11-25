import { NavLink, Outlet } from 'react-router-dom'
import { useI18n, type Lang } from '../../i18n'
import { useAuth } from '../auth/store'
import { api } from '../api/client'
import { refreshTokenStore } from '../auth/store'
import { HealthBanner } from './HealthBanner'

const links = [['/', 'nav.scanner'], ['/positions', 'nav.positions'], ['/orders', 'nav.orders'], ['/signals', 'nav.signals'], ['/risk', 'nav.risk'], ['/settings', 'nav.settings']] as const

export function Layout() {
  const { t, lang, setLang } = useI18n()
  const clear = useAuth((s) => s.clear)
  const logout = async () => {
    const rt = refreshTokenStore.get()
    if (rt) await api.POST('/api/auth/logout', { body: { refreshToken: rt } }).catch(() => undefined)
    clear()
  }
  return (
    <>
      <a href="#main" className="skip">Skip to content</a>
      <header className="top">
        <strong>{t('appName')}</strong>
        <nav aria-label="Main">
          {links.map(([to, key]) => <NavLink key={to} to={to} end={to === '/'}>{t(key)}</NavLink>)}
        </nav>
        <label>
          <span className="sr">{t('common.language')}</span>
          <select value={lang} onChange={(e) => setLang(e.target.value as Lang)}>
            <option value="en">English</option>
            <option value="fa">فارسی</option>
          </select>
        </label>
        <button type="button" onClick={logout}>{t('common.logout')}</button>
      </header>
      <HealthBanner />
      <main id="main" tabIndex={-1}><Outlet /></main>
    </>
  )
}
