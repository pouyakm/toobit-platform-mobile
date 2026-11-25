import { useState } from 'react'
import { FlatList, View } from 'react-native'
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../shared/api/client'
import { useI18n } from '../../shared/i18n'
import { stateName } from '../../shared/lib/orderState'
import { Button, Card, Empty, ErrorView, Heading, Loading, Pill, Txt, refreshControl } from '../../ui/components'
import { Screen } from '../../ui/Screen'
import { space } from '../../ui/theme'

function Trail({ id }: { id: string }) {
  const { t } = useI18n()
  const ev = useQuery({ queryKey: ['events', id], queryFn: async () => unwrap(await api().GET('/api/orders/{id}/events', { params: { path: { id } } })) })
  return (
    <View accessibilityLabel={t('orders.trail')} style={{ gap: 2 }}>
      {ev.data?.map((e) => <Txt key={e.id} small>{`${stateName(e.from)} → ${stateName(e.to)}${e.reason ? `  (${e.reason})` : ''}`}</Txt>)}
    </View>
  )
}

export default function Orders() {
  const { t } = useI18n()
  const [open, setOpen] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['orders'], queryFn: async () => unwrap(await api().GET('/api/orders')), refetchInterval: 5000 })
  return (
    <Screen>
      {q.isLoading ? <Loading /> : q.isError ? <ErrorView onRetry={() => void q.refetch()} /> : (
        <FlatList
          data={q.data ?? []}
          keyExtractor={(o) => String(o.id)}
          refreshControl={refreshControl(q)}
          ListHeaderComponent={<Heading>{t('orders.title')}</Heading>}
          ListEmptyComponent={<Empty />}
          contentContainerStyle={{ padding: space.lg, gap: space.sm }}
          renderItem={({ item: o }) => {
            const id = String(o.id)
            const st = stateName(o.stateMachineState)
            return (
              <Card>
                <Txt bold>{o.symbol}</Txt>
                <Pill text={st} tone={st === 'Rejected' || st === 'Unknown' ? 'danger' : st === 'Filled' ? 'ok' : 'neutral'} />
                <Txt small muted>{`${t('orders.retries')}: ${o.unknownRetries ?? 0}  ·  ${o.createdAt ?? ''}`}</Txt>
                <Button title={t('orders.trail')} pressed={open === id} onPress={() => setOpen(open === id ? null : id)} />
                {open === id && <Trail id={id} />}
              </Card>
            )
          }}
        />
      )}
    </Screen>
  )
}
