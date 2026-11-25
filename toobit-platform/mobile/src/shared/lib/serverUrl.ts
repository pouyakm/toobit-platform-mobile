/**
 * The app sends the operator's password and a bearer token to whatever server URL is configured, so the URL is validated:
 *  - https:// is always accepted.
 *  - http:// is accepted ONLY for loopback, the Android emulator host (10.0.2.2), link-local-free private LAN ranges, and *.local.
 *    (Cleartext to a public host would expose credentials; the OS also blocks it unless ALLOW_CLEARTEXT=1 at build time.)
 * Returns the normalized origin (no trailing slash, no path) or an error code the UI can translate.
 */
export type UrlCheck = { ok: true; url: string } | { ok: false; error: 'invalid' | 'scheme' | 'cleartext_public' }

const privateV4 = (h: string): boolean => {
  const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(h)
  if (!m) return false
  const [a, b] = [Number(m[1]), Number(m[2])]
  if ([a, b, Number(m[3]), Number(m[4])].some((n) => n > 255)) return false
  return a === 10 || a === 127 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168)
}

export function isLocalHost(host: string): boolean {
  const h = host.toLowerCase()
  return h === 'localhost' || h === '[::1]' || h.endsWith('.local') || privateV4(h)
}

export function checkServerUrl(input: string): UrlCheck {
  const raw = input.trim()
  if (!raw) return { ok: false, error: 'invalid' }
  const m = /^([a-z][a-z0-9+.-]*):\/\/([^/?#]+)/i.exec(raw)
  if (!m) return { ok: false, error: 'invalid' }
  const scheme = m[1].toLowerCase()
  const authority = m[2]
  if (authority.includes('@')) return { ok: false, error: 'invalid' } // no user:pass@host
  if (scheme !== 'http' && scheme !== 'https') return { ok: false, error: 'scheme' }
  const host = authority.startsWith('[') ? authority.slice(0, authority.indexOf(']') + 1) : authority.split(':')[0]
  if (!host) return { ok: false, error: 'invalid' }
  if (scheme === 'http' && !isLocalHost(host)) return { ok: false, error: 'cleartext_public' }
  return { ok: true, url: `${scheme}://${authority}` }
}
