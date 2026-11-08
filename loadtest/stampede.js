import http from 'k6/http';
import { check } from 'k6';

const API = __ENV.API || 'http://localhost:8080';

export const options = {
  scenarios: {
    stampede: {
      executor: 'per-vu-iterations',
      vus: 500,
      iterations: 1,
      maxDuration: '60s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<3000'],
  },
};

export function setup() {
  const r = http.post(
    `${API}/classes`,
    JSON.stringify({ name: `k6-stampede-${Date.now()}`, totalSeats: 120 }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  if (r.status !== 201) {
    throw new Error(`Failed to create class in setup: ${r.status} ${r.body}`);
  }
  return { classId: r.json('id') };
}

export default function (d) {
  // 17-digit birth registration student ID
  const sid = '2015' + String(__VU).padStart(6, '0') + String(__ITER).padStart(7, '0');
  const res = http.post(
    `${API}/classes/${d.classId}/holds`,
    null,
    {
      headers: { 'X-Student-Id': sid },
      responseCallback: http.expectedStatuses(201, 409),
    }
  );

  check(res, {
    'is 201 or 409': (r) => r.status === 201 || r.status === 409,
  });
}

export function teardown(d) {
  const c = http.get(`${API}/classes/${d.classId}`).json();
  if (c.heldSeats + c.confirmedSeats > c.totalSeats) {
    throw new Error(`OVERSOLD! held: ${c.heldSeats}, confirmed: ${c.confirmedSeats}, total: ${c.totalSeats}`);
  }
  console.log(`Teardown Invariant Verified: Class ${d.classId} total: ${c.totalSeats}, held: ${c.heldSeats}, confirmed: ${c.confirmedSeats}, available: ${c.availableSeats}`);
}
