import { describe, expect, it } from 'vitest'
import { en } from './en'
import { fa } from './fa'
import { lookup } from './index'

const keys = (o: object, p = ''): string[] =>
  Object.entries(o).flatMap(([k, v]) => (typeof v === 'object' && v !== null ? keys(v, `${p}${k}.`) : [`${p}${k}`]))

describe('i18n', () => {
  it('fa has exactly the same keys as en (no missing translations)', () => {
    expect(keys(fa).sort()).toEqual(keys(en).sort())
  })
  it('no translation is empty and placeholders match across languages', () => {
    for (const k of keys(en)) {
      const a = lookup(en, k), b = lookup(fa, k)
      expect(b.trim().length, k).toBeGreaterThan(0)
      expect(b.match(/\{\w+\}/g)?.sort(), k).toEqual(a.match(/\{\w+\}/g)?.sort())
    }
  })
  it('interpolates variables and returns the key when missing', () => {
    expect(lookup(en, 'positions.closeConfirm', { symbol: 'BTC-SWAP-USDT' })).toContain('BTC-SWAP-USDT')
    expect(lookup(en, 'nope.nothing')).toBe('nope.nothing')
  })
})
