// k6 load test: sustained dashboard + scanner traffic against the API (acceptance: load test at configured cadence).
// Run: k6 run -e BASE=http://localhost:8080 -e EMAIL=... -e PASSWORD=... loadtest/scanner.js
import http from 'k6/http'
import { check, sleep } from 'k6'

export const options = {
  scenarios: { dashboards: { executor: 'constant-vus', vus: 25, duration: '5m' } },
  thresholds: { http_req_failed: ['rate<0.01'], http_req_duration: ['p(95)<500', 'p(99)<1000'] },
}

export function setup() {
  const r = http.post(`${__ENV.BASE}/api/auth/login`, JSON.stringify({ email: __ENV.EMAIL, password: __ENV.PASSWORD }), { headers: { 'Content-Type': 'application/json' } })
  return { token: r.json('accessToken') }
}

export default function (d) {
  const h = { headers: { Authorization: `Bearer ${d.token}` } }
  for (const path of ['/api/scanner/top?n=20', '/api/prices', '/api/orders', '/api/signals']) {
    const r = http.get(`${__ENV.BASE}${path}`, h)
    check(r, { [`${path} 200`]: (x) => x.status === 200 })
  }
  sleep(2)
}
