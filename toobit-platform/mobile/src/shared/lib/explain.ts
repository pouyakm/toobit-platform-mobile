/** Defensive parse: ExplainJson is produced by another service and must never crash a screen. */
export function topFeatures(explainJson: string | undefined): { name: string; contribution: number }[] {
  try {
    const v = JSON.parse(explainJson ?? '{}') as { top_features?: { name: unknown; contribution: unknown }[] }
    if (!Array.isArray(v.top_features)) return []
    return v.top_features.filter((f) => typeof f?.name === 'string' && typeof f?.contribution === 'number') as { name: string; contribution: number }[]
  } catch {
    return []
  }
}
