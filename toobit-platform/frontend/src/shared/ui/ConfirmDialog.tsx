import { useEffect, useRef, useState } from 'react'
import { useI18n } from '../../i18n'

interface Props {
  open: boolean
  title: string
  message: string
  /** When set, the user must type this word before Confirm enables (kill-switch confirm step). */
  requireWord?: string
  onConfirm: (typed: string) => void
  onCancel: () => void
}

/** Accessible modal (WCAG AA): role=dialog, labelled, focus moved in, Escape closes, focus restored on close. */
export function ConfirmDialog({ open, title, message, requireWord, onConfirm, onCancel }: Props) {
  const { t } = useI18n()
  const [typed, setTyped] = useState('')
  const first = useRef<HTMLElement | null>(null)
  const opener = useRef<Element | null>(null)

  useEffect(() => {
    if (open) { opener.current = document.activeElement; setTyped(''); setTimeout(() => first.current?.focus(), 0) }
    else if (opener.current instanceof HTMLElement) opener.current.focus()
  }, [open])

  if (!open) return null
  const ok = !requireWord || typed === requireWord
  return (
    <div className="overlay" onKeyDown={(e) => e.key === 'Escape' && onCancel()}>
      <div role="dialog" aria-modal="true" aria-labelledby="dlg-title" aria-describedby="dlg-msg" className="dialog">
        <h2 id="dlg-title">{title}</h2>
        <p id="dlg-msg">{message}</p>
        {requireWord && (
          <input aria-label={requireWord} value={typed} onChange={(e) => setTyped(e.target.value)} ref={(el) => { first.current = el }} autoComplete="off" />
        )}
        <div className="row">
          <button type="button" onClick={onCancel} ref={(el) => { if (!requireWord) first.current = el }}>{t('common.cancel')}</button>
          <button type="button" className="danger" disabled={!ok} onClick={() => onConfirm(typed)}>{t('common.confirm')}</button>
        </div>
      </div>
    </div>
  )
}
