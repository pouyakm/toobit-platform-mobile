import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../api/client'
import { useI18n } from '../../i18n'

/** Status banner: kill switch, WS disconnected, market data stale, scanner stale. role=alert so screen readers announce changes. */
export function HealthBanner() {
  const { t } = useI18n()
  const prices = useQuery({ queryKey: ['prices'], queryFn: async () => unwrap(await api.GET('/api/prices')), refetchInterval: 3000 })
  const kill = useQuery({ queryKey: ['kill'], queryFn: async () => unwrap(await api.GET('/api/killswitch')), refetchInterval: 5000 })
  const msgs: string[] = []
  if (kill.data?.on) msgs.push(t('banner.killOn'))
  if (prices.data && !prices.data.wsConnected) msgs.push(t('banner.wsDown'))
  else if (prices.data?.marketDataStale) msgs.push(t('banner.marketStale'))
  if (prices.data?.scannerStale) msgs.push(t('banner.scannerStale'))
  return (
    <div role="alert" aria-live="polite" className={msgs.length ? 'banner warn' : 'banner'} hidden={msgs.length === 0}>
      {msgs.map((m) => <div key={m}>{m}</div>)}
    </div>
  )
}
