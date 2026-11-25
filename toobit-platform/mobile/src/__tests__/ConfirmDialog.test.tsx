import { fireEvent, render, screen } from '@testing-library/react-native'
import { ConfirmDialog } from '../ui/components'

const base = { open: true, title: 'Kill switch', message: 'Type HALT to confirm.', onConfirm: jest.fn(), onCancel: jest.fn() }
beforeEach(() => jest.clearAllMocks())

describe('ConfirmDialog', () => {
  it('requires the typed word before Confirm works', async () => {
    await render(<ConfirmDialog {...base} requireWord="HALT" />)
    const confirm = screen.getByRole('button', { name: 'Confirm' })
    await fireEvent.press(confirm)
    expect(base.onConfirm).not.toHaveBeenCalled()
    await fireEvent.changeText(screen.getByLabelText('HALT'), 'HALT')
    await fireEvent.press(screen.getByRole('button', { name: 'Confirm' }))
    expect(base.onConfirm).toHaveBeenCalledWith('HALT')
  })
  it('confirms immediately without a required word, and cancels', async () => {
    await render(<ConfirmDialog {...base} />)
    await fireEvent.press(screen.getByRole('button', { name: 'Confirm' }))
    expect(base.onConfirm).toHaveBeenCalled()
    await fireEvent.press(screen.getByRole('button', { name: 'Cancel' }))
    expect(base.onCancel).toHaveBeenCalled()
  })
})
