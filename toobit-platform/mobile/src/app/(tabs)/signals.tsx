import { FlatList } from 'react-native'
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../shared/api/client'
import { useI18n } from '../../shared/i18n'
import { topFeatures } from '../../shared/lib/explain'
import { Card, Empty, ErrorView, Heading, Loading, Txt, refreshControl } from '../../ui/components'
import { Screen } from '../../ui/Screen'
import { space } from '../../ui/theme'

export default function Signals() {
  const { t } = useI18n()
  const q = useQuery({ queryKey: ['signals'], queryFn: async () => unwrap(await api().GET('/api/signals')), refetchInterval: 10000 })
  return (
    <Screen>
      {q.isLoading ? <Loading /> : q.isError ? <ErrorView onRetry={() => void q.refetch()} /> : (
        <FlatList
          data={q.data ?? []}
          keyExtractor={(s) => String(s.id)}
          refreshControl={refreshControl(q)}
          ListHeaderComponent={<><Heading>{t('signals.title')}</Heading><Txt small muted>{t('signals.note')}</Txt></>}
          ListEmptyComponent={<Empty />}
          contentContainerStyle={{ padding: space.lg, gap: space.sm }}
          renderItem={({ item: s }) => (
            <Card>
              <Txt bold>{`${s.symbol}  ·  ${s.action}${s.shadow ? ` (${t('signals.shadow')})` : ''}`}</Txt>
              <Txt small muted>{`${t('signals.confidence')}: ${(s.confidence ?? 0).toFixed(2)}  ·  ${t('signals.model')}: ${s.modelVersion ?? '—'}`}</Txt>
              {topFeatures(s.explainJson).length > 0 && <Txt small>{topFeatures(s.explainJson).map((f) => `${f.name}: ${f.contribution}`).join(', ')}</Txt>}
            </Card>
          )}
        />
      )}
    </Screen>
  )
}
