import { ScrollView, View } from 'react-native'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../../shared/api/client'
import { isAdmin, useAuth } from '../../../shared/auth/store'
import { useI18n } from '../../../shared/i18n'
import { Button, Card, Empty, Heading, Pill, Txt } from '../../../ui/components'
import { Screen } from '../../../ui/Screen'
import { space } from '../../../ui/theme'

export default function Risk() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const admin = isAdmin(useAuth((s) => s.role))
  const paused = useQuery({ queryKey: ['pauses'], queryFn: async () => unwrap(await api().GET('/api/pauses')), refetchInterval: 10000 })
  const risk = useQuery({ queryKey: ['risk'], queryFn: async () => unwrap(await api().GET('/api/risk-events')), refetchInterval: 10000 })
  const recon = useQuery({ queryKey: ['recon'], queryFn: async () => unwrap(await api().GET('/api/reconciliation-events')), refetchInterval: 10000 })
  const clear = useMutation({
    mutationFn: async (symbol: string) => { await api().DELETE('/api/pauses/{symbol}', { params: { path: { symbol } } }) },
    onSettled: () => qc.invalidateQueries({ queryKey: ['pauses'] }),
  })
  return (
    <Screen banner={false}>
      <ScrollView contentContainerStyle={{ padding: space.lg, gap: space.sm }}>
        {(paused.data?.length ?? 0) > 0 && (
          <Card>
            <Txt bold>{t('risk.pausedList')}</Txt>
            {paused.data?.map((sym) => (
              <View key={sym} style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
                <Txt>{sym}</Txt>
                {admin && <Button title={t('risk.clearPause')} label={`${t('risk.clearPause')} ${sym}`} onPress={() => clear.mutate(sym)} />}
              </View>
            ))}
          </Card>
        )}
        <Heading>{t('risk.title')}</Heading>
        {risk.data?.length === 0 && <Empty />}
        {risk.data?.map((e) => (
          <Card key={e.id}><Txt bold>{e.symbol}</Txt><Pill text={String(e.rule)} tone="danger" /><Txt small>{e.reason}</Txt><Txt small muted>{e.createdAt}</Txt></Card>
        ))}
        <Heading>{t('risk.recon')}</Heading>
        {recon.data?.length === 0 && <Empty />}
        {recon.data?.map((e) => (
          <Card key={e.id}><Txt bold>{e.symbol}</Txt><Pill text={String(e.kind)} />{e.pausedSymbol ? <Txt small>{t('risk.paused')} ✓</Txt> : null}<Txt small>{e.detail}</Txt></Card>
        ))}
      </ScrollView>
    </Screen>
  )
}
