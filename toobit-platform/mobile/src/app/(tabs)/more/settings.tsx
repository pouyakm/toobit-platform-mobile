import { useState } from 'react'
import { ScrollView, Switch, View } from 'react-native'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../../shared/api/client'
import { isAdmin, useAuth } from '../../../shared/auth/store'
import { useI18n } from '../../../shared/i18n'
import { Button, Card, ConfirmDialog, Field, Heading, Pill, Txt } from '../../../ui/components'
import { Screen } from '../../../ui/Screen'
import { space } from '../../../ui/theme'

export default function Settings() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const admin = isAdmin(useAuth((s) => s.role))
  const settings = useQuery({ queryKey: ['settings'], queryFn: async () => unwrap(await api().GET('/api/settings')) })
  const kill = useQuery({ queryKey: ['kill'], queryFn: async () => unwrap(await api().GET('/api/killswitch')), refetchInterval: 5000 })
  const [autoDraft, setAuto] = useState<boolean | null>(null)
  const [weightsDraft, setWeights] = useState<string | null>(null)
  const auto = autoDraft ?? !!settings.data?.flags?.automatedTrading
  const weights = weightsDraft ?? settings.data?.flags?.scannerWeightSet ?? 'default'
  const [reason, setReason] = useState('')
  const [confirmOpen, setConfirmOpen] = useState(false)


  const sets = Array.from(new Set(['default', ...(((settings.data?.scanner as { weightSets?: string[] } | undefined)?.weightSets) ?? [])]))
  const saveFlags = useMutation({
    mutationFn: async () => { const r = await api().PUT('/api/settings/flags', { body: { automatedTrading: auto, scannerWeightSet: weights } }); if (r.response.status >= 400) throw new Error('rejected') },
    onSettled: () => { setAuto(null); setWeights(null); void qc.invalidateQueries({ queryKey: ['settings'] }) },
  })
  const toggleKill = useMutation({
    mutationFn: async (v: { on: boolean; confirm: string }) => unwrap(await api().POST('/api/killswitch', { body: { on: v.on, reason: reason || 'mobile', confirm: v.confirm } })),
    onSettled: () => qc.invalidateQueries({ queryKey: ['kill'] }),
  })
  const killOn = kill.data?.on ?? false
  const word = killOn ? 'RESUME' : 'HALT'

  return (
    <Screen banner={false}>
      <ScrollView contentContainerStyle={{ padding: space.lg, gap: space.md }}>
        <Heading>{t('settings.killTitle')}</Heading>
        <View accessibilityLiveRegion="polite"><Pill text={killOn ? 'ON' : 'OFF'} tone={killOn ? 'danger' : 'ok'} /></View>
        {admin && (
          <>
            <Field label={t('settings.reason')} value={reason} onChangeText={setReason} />
            <Button danger title={killOn ? t('settings.killOff') : t('settings.killOn')} onPress={() => setConfirmOpen(true)} />
            <ConfirmDialog open={confirmOpen} title={t('settings.killTitle')} message={t('settings.killHint', { word })} requireWord={word}
              onCancel={() => setConfirmOpen(false)} onConfirm={(typed) => { toggleKill.mutate({ on: !killOn, confirm: typed }); setConfirmOpen(false) }} />
          </>
        )}

        <Heading>{t('settings.flags')}</Heading>
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: 48 }}>
          <Txt>{t('settings.autoTrading')}</Txt>
          <Switch value={auto} disabled={!admin} onValueChange={setAuto} accessibilityLabel={t('settings.autoTrading')} />
        </View>
        <Txt bold>{t('settings.weightSet')}</Txt>
        <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.sm }}>
          {sets.map((w) => <Button key={w} title={w} pressed={weights === w} disabled={!admin} onPress={() => setWeights(w)} />)}
        </View>
        {admin && <Button title={t('settings.save')} onPress={() => saveFlags.mutate()} disabled={saveFlags.isPending} />}

        <Txt small muted>{t('settings.readonlyNote')}</Txt>
        <Card>
          {Object.entries(settings.data?.strategy ?? {}).map(([k, v]) => <Txt key={k} small>{`${k}: ${String(v)}`}</Txt>)}
        </Card>
      </ScrollView>
    </Screen>
  )
}
