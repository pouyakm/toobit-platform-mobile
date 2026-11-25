import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent } from 'react'
import { api, unwrap } from '../../shared/api/client'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { isAdmin, useAuth } from '../../shared/auth/store'
import { useI18n } from '../../i18n'

export function SettingsPage() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const role = useAuth((s) => s.role)
  const admin = isAdmin(role)
  const settings = useQuery({ queryKey: ['settings'], queryFn: async () => unwrap(await api.GET('/api/settings')) })
  const kill = useQuery({ queryKey: ['kill'], queryFn: async () => unwrap(await api.GET('/api/killswitch')), refetchInterval: 5000 })
  const [auto, setAuto] = useState(false)
  const [weights, setWeights] = useState('default')
  const [reason, setReason] = useState('')
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => { if (settings.data?.flags) { setAuto(settings.data.flags.automatedTrading); setWeights(settings.data.flags.scannerWeightSet) } }, [settings.data])

  const saveFlags = useMutation({
    mutationFn: async () => { const r = await api.PUT('/api/settings/flags', { body: { automatedTrading: auto, scannerWeightSet: weights } }); if (r.response.status >= 400) throw new Error('rejected') },
    onSettled: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
  const toggleKill = useMutation({
    mutationFn: async (v: { on: boolean; confirm: string }) => unwrap(await api.POST('/api/killswitch', { body: { on: v.on, reason: reason || 'dashboard', confirm: v.confirm } })),
    onSettled: () => qc.invalidateQueries({ queryKey: ['kill'] }),
  })
  const killOn = kill.data?.on ?? false
  const word = killOn ? 'RESUME' : 'HALT'

  return (
    <section aria-labelledby="se-title">
      <h1 id="se-title">{t('settings.title')}</h1>

      <h2>{t('settings.killTitle')}</h2>
      <p aria-live="polite"><strong>{killOn ? 'ON' : 'OFF'}</strong></p>
      {admin && (
        <>
          <label>{t('settings.reason')}<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <button type="button" className="danger" onClick={() => setConfirmOpen(true)}>{killOn ? t('settings.killOff') : t('settings.killOn')}</button>
          <ConfirmDialog open={confirmOpen} title={t('settings.killTitle')} message={t('settings.killHint', { word })} requireWord={word}
            onCancel={() => setConfirmOpen(false)} onConfirm={(typed) => { toggleKill.mutate({ on: !killOn, confirm: typed }); setConfirmOpen(false) }} />
        </>
      )}

      <h2>{t('settings.flags')}</h2>
      <form onSubmit={(e: FormEvent) => { e.preventDefault(); saveFlags.mutate() }}>
        <label><input type="checkbox" checked={auto} disabled={!admin} onChange={(e) => setAuto(e.target.checked)} /> {t('settings.autoTrading')}</label>
        <label>{t('settings.weightSet')}
          <select value={weights} disabled={!admin} onChange={(e) => setWeights(e.target.value)}>
            {(['default', ...((settings.data?.scanner as { weightSets?: string[] } | undefined)?.weightSets ?? [])]).filter((v, i, a) => a.indexOf(v) === i).map((w) => <option key={w} value={w}>{w}</option>)}
          </select>
        </label>
        {admin && <button type="submit">{t('settings.save')}</button>}
      </form>

      <p><small>{t('settings.readonlyNote')}</small></p>
      <dl>
        {Object.entries(settings.data?.strategy ?? {}).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{String(v)}</dd></div>)}
      </dl>
    </section>
  )
}
