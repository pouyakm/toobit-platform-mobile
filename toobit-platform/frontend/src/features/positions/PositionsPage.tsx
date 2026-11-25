import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, unwrap } from '../../shared/api/client'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { canTrade, useAuth } from '../../shared/auth/store'
import { useI18n } from '../../i18n'

export function PositionsPage() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const role = useAuth((s) => s.role)
  const [target, setTarget] = useState<{ symbol: string; price: number } | null>(null)
  const pos = useQuery({ queryKey: ['positions'], queryFn: async () => unwrap(await api.GET('/api/positions')), refetchInterval: 5000 })
  const prices = useQuery({ queryKey: ['prices'], queryFn: async () => unwrap(await api.GET('/api/prices')), refetchInterval: 3000 })

  const close = useMutation({
    mutationFn: async (v: { symbol: string; price: number }) =>
      unwrap(await api.POST('/api/positions/{symbol}/close', { params: { path: { symbol: v.symbol } }, body: { confirm: true, referencePrice: v.price } })),
    onSettled: () => qc.invalidateQueries({ queryKey: ['positions'] }),
  })

  if (pos.isLoading) return <p>{t('common.loading')}</p>
  if (pos.isError) return <p role="alert">{t('common.error')}</p>
  return (
    <section aria-labelledby="po-title">
      <h1 id="po-title">{t('positions.title')}</h1>
      {pos.data?.length === 0 ? <p>{t('common.none')}</p> : (
        <table>
          <thead><tr><th scope="col">{t('positions.symbol')}</th><th scope="col">{t('positions.side')}</th><th scope="col">{t('positions.qty')}</th><th scope="col">{t('positions.entry')}</th><th scope="col"><span className="sr">{t('positions.close')}</span></th></tr></thead>
          <tbody>{pos.data?.map((p) => (
            <tr key={p.id}><td>{p.symbol}</td><td>{p.side}</td><td>{p.quantity}</td><td>{p.entryPrice}</td>
              <td>{canTrade(role) && <button type="button" className="danger" onClick={() => setTarget({ symbol: p.symbol, price: prices.data?.items.find((i) => i.symbol === p.symbol)?.last ?? p.entryPrice ?? 0 })}>{t('positions.close')}</button>}</td></tr>
          ))}</tbody>
        </table>
      )}
      <ConfirmDialog open={target !== null} title={t('positions.close')} message={t('positions.closeConfirm', { symbol: target?.symbol ?? '' })}
        onCancel={() => setTarget(null)} onConfirm={() => { if (target) close.mutate(target); setTarget(null) }} />
    </section>
  )
}
