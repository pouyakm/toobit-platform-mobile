import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Fragment, useState } from 'react'
import { api, unwrap } from '../../shared/api/client'
import { canTrade, useAuth } from '../../shared/auth/store'
import { useI18n } from '../../i18n'

type Top = { stale: boolean; items: { rank: number; symbol: string; score: number; quoteVolume?: number; momentum?: number; volumeSpikeRatio?: number; normVolume?: number; normMomentum?: number; normSpike?: number; explanation?: string }[] }

const Bar = ({ label, v }: { label: string; v: number }) => (
  <div className="metric"><span>{label}</span><meter min={0} max={1} value={v} aria-label={`${label} ${v.toFixed(2)}`} /><span>{v.toFixed(2)}</span></div>
)

export function ScannerPage() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const role = useAuth((s) => s.role)
  const [open, setOpen] = useState<string | null>(null)
  const [pinned, setPinned] = useState<Set<string>>(new Set())

  const top = useQuery({ queryKey: ['top'], queryFn: async () => unwrap(await api.GET('/api/scanner/top', { params: { query: { n: 20 } } })) as Top, refetchInterval: 15000 })
  const prices = useQuery({ queryKey: ['prices'], queryFn: async () => unwrap(await api.GET('/api/prices')), refetchInterval: 3000 })
  const priceOf = new Map(prices.data?.items.map((p) => [p.symbol, p.last]))

  // Optimistic override: the pin toggles instantly and is rolled back if the server rejects it.
  const override = useMutation({
    mutationFn: async (v: { symbol: string; pinned: boolean }) => { const r = await api.POST('/api/scanner/overrides', { body: v }); if (r.response.status >= 400) throw new Error('rejected') },
    onMutate: async (v) => {
      await qc.cancelQueries({ queryKey: ['top'] })
      const prev = new Set(pinned)
      setPinned((s) => { const n = new Set(s); if (v.pinned) n.add(v.symbol); else n.delete(v.symbol); return n })
      return { prev }
    },
    onError: (_e, _v, ctx) => { if (ctx) setPinned(ctx.prev) },
    onSettled: () => qc.invalidateQueries({ queryKey: ['top'] }),
  })

  if (top.isLoading) return <p>{t('common.loading')}</p>
  if (top.isError || !top.data) return <p role="alert">{t('common.error')}</p>
  if (top.data.items.length === 0) return <p>{t('common.none')}</p>

  return (
    <section aria-labelledby="sc-title">
      <h1 id="sc-title">{t('scanner.title')}</h1>
      <table>
        <thead><tr>
          <th scope="col">{t('scanner.rank')}</th><th scope="col">{t('scanner.symbol')}</th><th scope="col">{t('scanner.score')}</th>
          <th scope="col">{t('scanner.price')}</th><th scope="col"><span className="sr">{t('scanner.why')}</span></th>{canTrade(role) && <th scope="col"><span className="sr">{t('scanner.pin')}</span></th>}
        </tr></thead>
        <tbody>
          {top.data.items.map((r) => (
            <Fragment key={r.symbol}>
              <tr>
                <td>{r.rank}</td><td>{r.symbol}</td><td>{r.score.toFixed(3)}</td><td>{priceOf.get(r.symbol) ?? '—'}</td>
                <td><button type="button" aria-expanded={open === r.symbol} onClick={() => setOpen(open === r.symbol ? null : r.symbol)}>{t('scanner.why')}</button></td>
                {canTrade(role) && <td><button type="button" aria-pressed={pinned.has(r.symbol)} onClick={() => override.mutate({ symbol: r.symbol, pinned: !pinned.has(r.symbol) })}>{pinned.has(r.symbol) ? t('scanner.unpin') : t('scanner.pin')}</button></td>}
              </tr>
              {open === r.symbol && (
                <tr><td colSpan={6}>
                  <h2>{t('scanner.breakdown')}</h2>
                  <Bar label={t('scanner.volume')} v={r.normVolume ?? 0} /><Bar label={t('scanner.momentum')} v={r.normMomentum ?? 0} /><Bar label={t('scanner.spike')} v={r.normSpike ?? 0} />
                  <code>{r.explanation}</code>
                </td></tr>
              )}
            </Fragment>
          ))}
        </tbody>
      </table>
    </section>
  )
}
