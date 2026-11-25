import { checkServerUrl } from '../shared/lib/serverUrl'
import { stateName } from '../shared/lib/orderState'
import { topFeatures } from '../shared/lib/explain'
import { en } from '../shared/i18n/en'
import { fa } from '../shared/i18n/fa'
import { lookup } from '../shared/i18n'

const keys = (o: object, p = ''): string[] =>
  Object.entries(o).flatMap(([k, v]) => (typeof v === 'object' && v ? keys(v, `${p}${k}.`) : [`${p}${k}`]))

describe('i18n', () => {
  it('en and fa have identical keys', () => { expect(keys(fa).sort()).toEqual(keys(en).sort()) })
  it('interpolates and falls back to the key', () => {
    expect(lookup(en, 'settings.killHint', { word: 'HALT' })).toBe('Type HALT to confirm.')
    expect(lookup(en, 'nope.missing')).toBe('nope.missing')
  })
})

describe('checkServerUrl', () => {
  it.each(['https://trading.example.com', 'http://192.168.1.20:8080', 'http://10.0.2.2:8080', 'http://localhost:8080', 'http://172.16.0.5'])('accepts %s', (u) => {
    expect(checkServerUrl(u).ok).toBe(true)
  })
  it.each(['http://example.com', 'http://8.8.8.8', 'http://172.32.0.1'])('rejects cleartext public %s', (u) => {
    expect(checkServerUrl(u)).toEqual({ ok: false, error: 'cleartext_public' })
  })
  it('rejects bad schemes, garbage and embedded credentials', () => {
    expect(checkServerUrl('ftp://x.com').ok).toBe(false)
    expect(checkServerUrl('not a url').ok).toBe(false)
    expect(checkServerUrl('https://user:pw@example.com').ok).toBe(false)
  })
})

describe('orderState', () => {
  it('maps enum indices', () => {
    expect(stateName(0)).toBe('Received')
    expect(stateName(9)).toBe('Unknown')
    expect(stateName(42)).toBe('42')
    expect(stateName(undefined)).toBe('')
  })
})

describe('topFeatures', () => {
  it('parses valid, tolerates garbage', () => {
    expect(topFeatures('{"top_features":[{"name":"iv","contribution":0.3}]}')).toEqual([{ name: 'iv', contribution: 0.3 }])
    expect(topFeatures('{oops')).toEqual([])
    expect(topFeatures(undefined)).toEqual([])
    expect(topFeatures('{"top_features":"x"}')).toEqual([])
  })
})
