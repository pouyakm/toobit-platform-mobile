import { useState } from 'react'
import { FlatList, Pressable, View } from 'react-native'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../shared/api/client'
import { canTrade, useAuth } from '../../shared/auth/store'
import { useI18n } from '../../shared/i18n'
import { Button, Card, Empty, ErrorView, Heading, Loading, Meter, Txt, refreshControl } from '../../ui/components'
import { Screen } from '../../ui/Screen'
import { space } from '../../ui/theme'

export default function Scanner() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const role = useAuth((s) => s.role)
  const [open, setOpen] = useState<string | null>(null)
  const [pinned, setPinned] = useState<Set<string>>(new Set())
  const top = useQuery({ queryKey: ['top'], queryFn: async () => unwrap(await api().GET('/api/scanner/top', { params: { query: { n: 20 } } })), refetchInterval: 15000 })
  const prices = useQuery({ queryKey: ['prices'], queryFn: async () => unwrap(await api().GET('/api/prices')), refetchInterval: 3000 })
  const priceOf = new Map(prices.data?.items?.map((p) => [p.symbol, p.last]))

  // Optimistic pin with rollback if the server rejects it.
  const override = useMutation({
    mutationFn: async (v: { symbol: string; pinned: boolean }) => { const r = await api().POST('/api/scanner/overrides', { body: v }); if (r.response.status >= 400) throw new Error('rejected') },
    onMutate: async (v) => {
      await qc.cancelQueries({ queryKey: ['top'] })
      const prev = new Set(pinned)
      setPinned((s) => { const n = new Set(s); if (v.pinned) n.add(v.symbol); else n.delete(v.symbol); return n })
      return { prev }
    },
    onError: (_e, _v, ctx) => { if (ctx) setPinned(ctx.prev) },
    onSettled: () => qc.invalidateQueries({ queryKey: ['top'] }),
  })

  return (
    <Screen>
      {top.isLoading ? <Loading /> : top.isError ? <ErrorView onRetry={() => void top.refetch()} /> : (
        <FlatList
          data={top.data?.items ?? []}
          keyExtractor={(r) => r.symbol ?? String(r.rank)}
          refreshControl={refreshControl(top)}
          ListHeaderComponent={<Heading>{t('scanner.title')}</Heading>}
          ListEmptyComponent={<Empty />}
          contentContainerStyle={{ padding: space.lg, gap: space.sm }}
          renderItem={({ item: r }) => {
            const sym = r.symbol ?? ''
            const isOpen = open === sym
            return (
              <Card>
                <Pressable accessibilityRole="button" accessibilityState={{ expanded: isOpen }} accessibilityLabel={`${t('scanner.rank')} ${r.rank} ${sym}, ${t('scanner.score')} ${(r.score ?? 0).toFixed(3)}. ${t('scanner.why')}`}
                  onPress={() => setOpen(isOpen ? null : sym)} style={{ minHeight: 48, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
                  <Txt bold>{`#${r.rank}  ${sym}`}</Txt>
                  <View style={{ alignItems: 'flex-end' }}>
                    <Txt>{(r.score ?? 0).toFixed(3)}</Txt>
                    <Txt small muted>{String(priceOf.get(sym) ?? '—')}</Txt>
                  </View>
                </Pressable>
                {isOpen && (
                  <View style={{ gap: space.sm }}>
                    <Txt small muted>{t('scanner.breakdown')}</Txt>
                    <Meter label={t('scanner.volume')} value={r.normVolume ?? 0} />
                    <Meter label={t('scanner.momentum')} value={r.normMomentum ?? 0} />
                    <Meter label={t('scanner.spike')} value={r.normSpike ?? 0} />
                    {!!r.explanation && <Txt small muted>{r.explanation}</Txt>}
                  </View>
                )}
                {canTrade(role) && (
                  <Button title={pinned.has(sym) ? t('scanner.unpin') : t('scanner.pin')} pressed={pinned.has(sym)} onPress={() => override.mutate({ symbol: sym, pinned: !pinned.has(sym) })} />
                )}
              </Card>
            )
          }}
        />
      )}
    </Screen>
  )
}
