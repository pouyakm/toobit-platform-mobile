import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { I18nProvider } from '../../i18n'
import { ConfirmDialog } from './ConfirmDialog'

const wrap = (ui: React.ReactElement) => render(<I18nProvider>{ui}</I18nProvider>)

describe('ConfirmDialog (kill-switch confirm step)', () => {
  it('is an accessible modal dialog', () => {
    wrap(<ConfirmDialog open title="T" message="M" onConfirm={() => {}} onCancel={() => {}} />)
    const d = screen.getByRole('dialog')
    expect(d.getAttribute('aria-modal')).toBe('true')
    expect(d.getAttribute('aria-labelledby')).toBe('dlg-title')
  })
  it('requires the exact word before Confirm enables', () => {
    const onConfirm = vi.fn()
    wrap(<ConfirmDialog open title="T" message="M" requireWord="HALT" onConfirm={onConfirm} onCancel={() => {}} />)
    const btn = screen.getByRole('button', { name: 'Confirm' }) as HTMLButtonElement
    expect(btn.disabled).toBe(true)
    fireEvent.change(screen.getByLabelText('HALT'), { target: { value: 'halt' } })
    expect(btn.disabled).toBe(true)
    fireEvent.change(screen.getByLabelText('HALT'), { target: { value: 'HALT' } })
    expect(btn.disabled).toBe(false)
    fireEvent.click(btn); expect(onConfirm).toHaveBeenCalledWith('HALT')
  })
  it('Escape cancels and nothing renders when closed', () => {
    const onCancel = vi.fn()
    const { container, rerender } = wrap(<ConfirmDialog open title="T" message="M" onConfirm={() => {}} onCancel={onCancel} />)
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' }); expect(onCancel).toHaveBeenCalled()
    rerender(<I18nProvider><ConfirmDialog open={false} title="T" message="M" onConfirm={() => {}} onCancel={() => {}} /></I18nProvider>)
    expect(container.querySelector('[role=dialog]')).toBeNull()
  })
})
