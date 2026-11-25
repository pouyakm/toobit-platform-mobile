import { useState } from 'react'
import { FlatList } from 'react-native'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../shared/api/client'
import { canTrade, useAuth } from '../../shared/auth/store'
import { useI18n } from '../../shared/i18n'
import { Button, Card, ConfirmDialog, Empty, ErrorView, Heading, Loading, Pill, Txt, refreshControl } from '../../ui/components'
import { Screen } from '../../ui/Screen'
import { space } from '../../ui/theme'

export default function Positions() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const role = useAuth((s) => s.role)
  const [target, setTarget] = useState<{ symbol: string; price: number } | null>(null)
  const pos = useQuery({ queryKey: ['positions'], queryFn: async () => unwrap(await api().GET('/api/positions')), refetchInterval: 5000 })
  const prices = useQuery({ queryKey: ['prices'], queryFn: async () => unwrap(await api().GET('/api/prices')), refetchInterval: 3000 })
  const close = useMutation({
    mutationFn: async (v: { symbol: string; price: number }) =>
      unwrap(await api().POST('/api/positions/{symbol}/close', { params: { path: { symbol: v.symbol } }, body: { confirm: true, referencePrice: v.price } })),
    onSettled: () => qc.invalidateQueries({ queryKey: ['positions'] }),
  })
  return (
    <Screen>
      {pos.isLoading ? <Loading /> : pos.isError ? <ErrorView onRetry={() => void pos.refetch()} /> : (
        <FlatList
          data={pos.data ?? []}
          keyExtractor={(p) => String(p.id)}
          refreshControl={refreshControl(pos)}
          ListHeaderComponent={<Heading>{t('positions.title')}</Heading>}
          ListEmptyComponent={<Empty />}
          contentContainerStyle={{ padding: space.lg, gap: space.sm }}
          renderItem={({ item: p }) => (
            <Card>
              <Txt bold>{p.symbol}</Txt>
              <Pill text={String(p.side)} />
              <Txt small muted>{`${t('positions.qty')}: ${p.quantity}  ·  ${t('positions.entry')}: ${p.entryPrice ?? '—'}`}</Txt>
              {canTrade(role) && (
                <Button danger title={t('positions.close')} label={`${t('positions.close')} ${p.symbol}`}
                  onPress={() => setTarget({ symbol: p.symbol ?? '', price: prices.data?.items?.find((i) => i.symbol === p.symbol)?.last ?? p.entryPrice ?? 0 })} />
              )}
            </Card>
          )}
        />
      )}
      <ConfirmDialog open={target !== null} title={t('positions.close')} message={t('positions.closeConfirm', { symbol: target?.symbol ?? '' })}
        onCancel={() => setTarget(null)} onConfirm={() => { if (target) close.mutate(target); setTarget(null) }} />
    </Screen>
  )
}
