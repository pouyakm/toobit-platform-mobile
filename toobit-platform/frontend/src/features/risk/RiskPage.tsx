import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../shared/api/client'
import { isAdmin, useAuth } from '../../shared/auth/store'
import { useI18n } from '../../i18n'

export function RiskPage() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const admin = isAdmin(useAuth((s) => s.role))
  const paused = useQuery({ queryKey: ['pauses'], queryFn: async () => unwrap(await api.GET('/api/pauses')), refetchInterval: 10000 })
  const clear = useMutation({
    mutationFn: async (symbol: string) => { await api.DELETE('/api/pauses/{symbol}', { params: { path: { symbol } } }) },
    onSettled: () => qc.invalidateQueries({ queryKey: ['pauses'] }),
  })
  const risk = useQuery({ queryKey: ['risk'], queryFn: async () => unwrap(await api.GET('/api/risk-events')), refetchInterval: 10000 })
  const recon = useQuery({ queryKey: ['recon'], queryFn: async () => unwrap(await api.GET('/api/reconciliation-events')), refetchInterval: 10000 })
  return (
    <section aria-labelledby="ri-title">
      <h1 id="ri-title">{t('risk.title')}</h1>
      {risk.data?.length === 0 ? <p>{t('common.none')}</p> : (
        <table><thead><tr><th scope="col">{t('orders.symbol')}</th><th scope="col">{t('risk.rule')}</th><th scope="col">{t('risk.reason')}</th><th scope="col">{t('orders.created')}</th></tr></thead>
          <tbody>{risk.data?.map((e) => <tr key={e.id}><td>{e.symbol}</td><td>{e.rule}</td><td>{e.reason}</td><td>{e.createdAt}</td></tr>)}</tbody></table>
      )}
      {(paused.data?.length ?? 0) > 0 && (
        <div role="status"><strong>{t('risk.pausedList')}</strong>
          <ul>{paused.data?.map((sym) => <li key={sym}>{sym} {admin && <button type="button" onClick={() => clear.mutate(sym)}>{t('risk.clearPause')}</button>}</li>)}</ul>
        </div>
      )}
      <h2>{t('risk.recon')}</h2>
      {recon.data?.length === 0 ? <p>{t('common.none')}</p> : (
        <table><thead><tr><th scope="col">{t('orders.symbol')}</th><th scope="col">{t('risk.kind')}</th><th scope="col">{t('risk.reason')}</th><th scope="col">{t('risk.paused')}</th></tr></thead>
          <tbody>{recon.data?.map((e) => <tr key={e.id}><td>{e.symbol}</td><td>{e.kind}</td><td>{e.detail}</td><td>{e.pausedSymbol ? '✓' : ''}</td></tr>)}</tbody></table>
      )}
    </section>
  )
}
