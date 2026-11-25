import { useState, type FormEvent } from 'react'
import { api } from '../../shared/api/client'
import { useAuth } from '../../shared/auth/store'
import { useI18n } from '../../i18n'

export function LoginPage() {
  const { t } = useI18n()
  const setSession = useAuth((s) => s.setSession)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(false)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault(); setBusy(true); setError(false)
    const { data } = await api.POST('/api/auth/login', { body: { email, password } })
    setBusy(false)
    if (data) setSession(data); else setError(true)
  }
  return (
    <main className="login">
      <form onSubmit={submit} aria-labelledby="login-title">
        <h1 id="login-title">{t('login.title')}</h1>
        <label>{t('login.email')}<input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} /></label>
        <label>{t('login.password')}<input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} /></label>
        {error && <p role="alert" className="err">{t('login.failed')}</p>}
        <button type="submit" disabled={busy}>{t('login.submit')}</button>
      </form>
    </main>
  )
}
