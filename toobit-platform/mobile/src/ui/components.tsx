import { type ReactNode, useState } from 'react'
import { ActivityIndicator, Modal, Pressable, RefreshControl, StyleSheet, Text, TextInput, View, type ViewStyle } from 'react-native'
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../shared/api/client'
import { useI18n } from '../shared/i18n'
import { MIN_TOUCH, space, useTheme } from './theme'

export function Txt({ children, bold, muted, small, style }: { children: ReactNode; bold?: boolean; muted?: boolean; small?: boolean; style?: object }) {
  const c = useTheme()
  return <Text style={[{ color: muted ? c.muted : c.fg, fontSize: small ? 13 : 16, fontWeight: bold ? '700' : '400' }, style]}>{children}</Text>
}

export function Heading({ children }: { children: ReactNode }) {
  const c = useTheme()
  return <Text accessibilityRole="header" style={{ color: c.fg, fontSize: 20, fontWeight: '700', marginVertical: space.sm }}>{children}</Text>
}

export function Card({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  const c = useTheme()
  return <View style={[{ backgroundColor: c.surface, borderColor: c.line, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10, padding: space.md, gap: space.xs }, style]}>{children}</View>
}

export function Button({ title, onPress, danger, disabled, pressed, label }: { title: string; onPress: () => void; danger?: boolean; disabled?: boolean; pressed?: boolean; label?: string }) {
  const c = useTheme()
  const color = danger ? c.danger : c.accent
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label ?? title}
      accessibilityState={{ disabled: !!disabled, selected: pressed }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed: p }) => ({ minHeight: MIN_TOUCH, minWidth: MIN_TOUCH, paddingHorizontal: space.lg, justifyContent: 'center', alignItems: 'center', borderRadius: 8, borderWidth: 1.5, borderColor: color, opacity: disabled ? 0.5 : p ? 0.7 : 1, backgroundColor: pressed ? color : 'transparent' })}
    >
      <Text style={{ color: pressed ? c.onAccent : color, fontWeight: '600', fontSize: 15 }}>{title}</Text>
    </Pressable>
  )
}

export function Field({ label, ...props }: { label: string } & React.ComponentProps<typeof TextInput>) {
  const c = useTheme()
  return (
    <View style={{ gap: space.xs }}>
      <Text style={{ color: c.fg, fontSize: 14, fontWeight: '600' }}>{label}</Text>
      <TextInput accessibilityLabel={label} placeholderTextColor={c.muted} {...props}
        style={{ minHeight: MIN_TOUCH, borderWidth: 1, borderColor: c.line, borderRadius: 8, paddingHorizontal: space.md, color: c.fg, backgroundColor: c.bg }} />
    </View>
  )
}

export function Pill({ text, tone }: { text: string; tone?: 'danger' | 'ok' | 'neutral' }) {
  const c = useTheme()
  const color = tone === 'danger' ? c.danger : tone === 'ok' ? c.ok : c.muted
  return <Text style={{ color, borderColor: color, borderWidth: 1, borderRadius: 99, paddingHorizontal: 8, paddingVertical: 1, fontSize: 12, fontWeight: '700', alignSelf: 'flex-start' }}>{text}</Text>
}

/** Normalized 0..1 meter (per-metric score breakdown). Announced as "<label> <value>" for screen readers. */
export function Meter({ label, value }: { label: string; value: number }) {
  const c = useTheme()
  const v = Math.max(0, Math.min(1, value))
  return (
    <View accessible accessibilityLabel={`${label} ${v.toFixed(2)}`} style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm }}>
      <View style={{ width: 96 }}><Txt small muted>{label}</Txt></View>
      <View style={{ flex: 1, height: 8, backgroundColor: c.line, borderRadius: 4, overflow: 'hidden' }}>
        <View style={{ width: `${v * 100}%`, height: 8, backgroundColor: c.accent }} />
      </View>
      <Txt small>{v.toFixed(2)}</Txt>
    </View>
  )
}

export function Loading() {
  const { t } = useI18n()
  return <View style={{ padding: space.xl, alignItems: 'center' }} accessibilityLiveRegion="polite"><ActivityIndicator accessibilityLabel={t('common.loading')} /></View>
}

export function ErrorView({ onRetry }: { onRetry?: () => void }) {
  const { t } = useI18n()
  const c = useTheme()
  return (
    <View style={{ padding: space.xl, gap: space.md, alignItems: 'center' }} accessibilityRole="alert">
      <Text style={{ color: c.danger, fontWeight: '600' }}>{t('common.error')}</Text>
      {onRetry && <Button title={t('common.retry')} onPress={onRetry} />}
    </View>
  )
}

export function Empty() {
  const { t } = useI18n()
  return <View style={{ padding: space.xl, alignItems: 'center' }}><Txt muted>{t('common.none')}</Txt></View>
}

/** Pull-to-refresh control bound to a TanStack Query result. */
export function refreshControl(q: { isRefetching: boolean; refetch: () => unknown }) {
  return <RefreshControl refreshing={q.isRefetching} onRefresh={() => void q.refetch()} />
}

/**
 * Status banner: kill switch, WS disconnected, market data stale, scanner stale.
 * accessibilityLiveRegion makes TalkBack announce changes; polling pauses automatically when the app is backgrounded.
 */
export function HealthBanner() {
  const { t } = useI18n()
  const c = useTheme()
  const prices = useQuery({ queryKey: ['prices'], queryFn: async () => unwrap(await api().GET('/api/prices')), refetchInterval: 3000 })
  const kill = useQuery({ queryKey: ['kill'], queryFn: async () => unwrap(await api().GET('/api/killswitch')), refetchInterval: 5000 })
  const msgs: string[] = []
  if (kill.data?.on) msgs.push(t('banner.killOn'))
  if (prices.data && !prices.data.wsConnected) msgs.push(t('banner.wsDown'))
  else if (prices.data?.marketDataStale) msgs.push(t('banner.marketStale'))
  if (prices.data?.scannerStale) msgs.push(t('banner.scannerStale'))
  if (msgs.length === 0) return null
  return (
    <View accessibilityRole="alert" accessibilityLiveRegion="polite" style={{ backgroundColor: c.warnBg, paddingHorizontal: space.lg, paddingVertical: space.sm, gap: 2 }}>
      {msgs.map((m) => <Text key={m} style={{ color: c.warnFg, fontWeight: '600' }}>{m}</Text>)}
    </View>
  )
}

interface DialogProps {
  open: boolean
  title: string
  message: string
  /** The user must type this exact word before Confirm enables (kill-switch confirm step). */
  requireWord?: string
  onConfirm: (typed: string) => void
  onCancel: () => void
}

/** Accessible modal: Android back closes it, typed-word gate for destructive actions. Body mounts only while open, so typed text resets each time. */
export function ConfirmDialog(props: DialogProps) {
  return (
    <Modal visible={props.open} transparent animationType="fade" onRequestClose={props.onCancel} accessibilityViewIsModal>
      {props.open && <DialogBody {...props} />}
    </Modal>
  )
}

function DialogBody({ title, message, requireWord, onConfirm, onCancel }: DialogProps) {
  const { t } = useI18n()
  const c = useTheme()
  const [typed, setTyped] = useState('')
  const ok = !requireWord || typed === requireWord
  return (
    <View style={{ flex: 1, backgroundColor: 'rgba(0,0,0,0.55)', justifyContent: 'center', padding: space.lg }}>
      <View style={{ backgroundColor: c.bg, borderRadius: 12, padding: space.lg, gap: space.md }}>
        <Text accessibilityRole="header" style={{ color: c.fg, fontSize: 18, fontWeight: '700' }}>{title}</Text>
        <Text style={{ color: c.fg }}>{message}</Text>
        {requireWord && <Field label={requireWord} value={typed} onChangeText={setTyped} autoCapitalize="characters" autoCorrect={false} autoComplete="off" />}
        <View style={{ flexDirection: 'row', gap: space.sm, justifyContent: 'flex-end' }}>
          <Button title={t('common.cancel')} onPress={onCancel} />
          <Button title={t('common.confirm')} danger disabled={!ok} onPress={() => onConfirm(typed)} />
        </View>
      </View>
    </View>
  )
}
