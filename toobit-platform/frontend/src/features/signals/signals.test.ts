import { describe, expect, it } from 'vitest'
import { topFeatures } from './SignalsPage'
import { stateName } from '../orders/OrdersPage'

describe('defensive parsing', () => {
  it('extracts top features from the Python explain payload', () => {
    expect(topFeatures('{"top_features":[{"name":"z","contribution":1.2}]}')).toEqual([{ name: 'z', contribution: 1.2 }])
  })
  it('never throws on malformed or missing explain JSON', () => {
    expect(topFeatures('not json')).toEqual([]); expect(topFeatures(undefined)).toEqual([]); expect(topFeatures('{"top_features":5}')).toEqual([])
  })
  it('maps OrderState indices to names, with Unknown last', () => {
    expect(stateName(0)).toBe('Received'); expect(stateName(9)).toBe('Unknown'); expect(stateName(99)).toBe('99')
  })
})
