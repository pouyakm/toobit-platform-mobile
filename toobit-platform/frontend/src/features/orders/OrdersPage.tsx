import { useQuery } from '@tanstack/react-query'
import { Fragment, useState } from 'react'
import { api, unwrap } from '../../shared/api/client'
import { useI18n } from '../../i18n'

// Mirrors Toobit.Platform.Domain.Orders.OrderState (JSON enum index).
export const ORDER_STATES = ['Received', 'Validated', 'RiskChecked', 'Submitted', 'Acked', 'PartiallyFilled', 'Filled', 'Canceled', 'Rejected', 'Unknown'] as const
export const stateName = (i: number | undefined) => (i === undefined ? '' : ORDER_STATES[i] ?? String(i))

function Trail({ id }: { id: string }) {
  const { t } = useI18n()
  const ev = useQuery({ queryKey: ['events', id], queryFn: async () => unwrap(await api.GET('/api/orders/{id}/events', { params: { path: { id } } })) })
  return (
    <ol aria-label={t('orders.trail')}>
      {ev.data?.map((e) => <li key={e.id}>{stateName(e.from)} → {stateName(e.to)} <small>{e.reason}</small></li>)}
    </ol>
  )
}

export function OrdersPage() {
  const { t } = useI18n()
  const [open, setOpen] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['orders'], queryFn: async () => unwrap(await api.GET('/api/orders')), refetchInterval: 5000 })
  if (q.isLoading) return <p>{t('common.loading')}</p>
  if (q.isError) return <p role="alert">{t('common.error')}</p>
  return (
    <section aria-labelledby="or-title">
      <h1 id="or-title">{t('orders.title')}</h1>
      {q.data?.length === 0 ? <p>{t('common.none')}</p> : (
        <table>
          <thead><tr><th scope="col">{t('orders.symbol')}</th><th scope="col">{t('orders.state')}</th><th scope="col">{t('orders.retries')}</th><th scope="col">{t('orders.created')}</th><th scope="col"><span className="sr">{t('orders.trail')}</span></th></tr></thead>
          <tbody>{q.data?.map((o) => (
            <Fragment key={o.id}>
              <tr><td>{o.symbol}</td><td><span className={`pill s-${stateName(o.stateMachineState)}`}>{stateName(o.stateMachineState)}</span></td><td>{o.unknownRetries ?? 0}</td><td>{o.createdAt}</td>
                <td><button type="button" aria-expanded={open === o.id} onClick={() => setOpen(open === o.id ? null : o.id)}>{t('orders.trail')}</button></td></tr>
              {open === o.id && <tr><td colSpan={5}><Trail id={o.id} /></td></tr>}
            </Fragment>
          ))}</tbody>
        </table>
      )}
    </section>
  )
}
