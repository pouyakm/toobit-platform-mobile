import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../shared/api/client'
import { useI18n } from '../../i18n'

/** Defensive parse: ExplainJson is produced by another service and must never crash the page. */
export function topFeatures(explainJson: string | undefined): { name: string; contribution: number }[] {
  try {
    const v = JSON.parse(explainJson ?? '{}') as { top_features?: { name: string; contribution: number }[] }
    return Array.isArray(v.top_features) ? v.top_features : []
  } catch { return [] }
}

export function SignalsPage() {
  const { t } = useI18n()
  const q = useQuery({ queryKey: ['signals'], queryFn: async () => unwrap(await api.GET('/api/signals')), refetchInterval: 10000 })
  if (q.isLoading) return <p>{t('common.loading')}</p>
  if (q.isError) return <p role="alert">{t('common.error')}</p>
  return (
    <section aria-labelledby="si-title">
      <h1 id="si-title">{t('signals.title')}</h1>
      <p><small>{t('signals.note')}</small></p>
      {q.data?.length === 0 ? <p>{t('common.none')}</p> : (
        <table>
          <thead><tr><th scope="col">{t('orders.symbol')}</th><th scope="col">{t('signals.action')}</th><th scope="col">{t('signals.confidence')}</th><th scope="col">{t('signals.model')}</th><th scope="col">{t('signals.features')}</th></tr></thead>
          <tbody>{q.data?.map((s) => (
            <tr key={s.id}><td>{s.symbol}</td><td>{s.action}{s.shadow ? ` (${t('signals.shadow')})` : ''}</td><td>{(s.confidence ?? 0).toFixed(2)}</td><td>{s.modelVersion ?? '—'}</td>
              <td>{topFeatures(s.explainJson).map((f) => `${f.name}: ${f.contribution}`).join(', ')}</td></tr>
          ))}</tbody>
        </table>
      )}
    </section>
  )
}
