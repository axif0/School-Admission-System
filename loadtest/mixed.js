import http from 'k6/http';
import { check, sleep } from 'k6';

const API = __ENV.API || 'http://localhost:8080';

export const options = {
  scenarios: {
    poller: {
      executor: 'constant-vus',
      vus: 50,
      duration: '30s',
      exec: 'pollClasses',
    },
    applicants: {
      executor: 'ramping-vus',
      startVUs: 5,
      stages: [
        { duration: '10s', target: 25 },
        { duration: '15s', target: 25 },
        { duration: '5s', target: 0 },
      ],
      exec: 'applyFlow',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<2500'],
  },
};

export function setup() {
  const r = http.post(
    `${API}/classes`,
    JSON.stringify({ name: `k6-mixed-${Date.now()}`, totalSeats: 50 }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  return { classId: r.json('id') };
}

export function pollClasses() {
  const res = http.get(`${API}/classes`);
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(4);
}

export function applyFlow(d) {
  const sid = '2015' + String(__VU).padStart(6, '0') + String(Date.now()).slice(-7);
  const holdRes = http.post(
    `${API}/classes/${d.classId}/holds`,
    null,
    {
      headers: { 'X-Student-Id': sid },
      responseCallback: http.expectedStatuses(201, 409),
    }
  );

  if (holdRes.status === 201) {
    const holdId = holdRes.json('holdId');
    sleep(1);
    const confirmRes = http.post(
      `${API}/holds/${holdId}/confirm`,
      null,
      { headers: { 'X-Student-Id': sid } }
    );
    check(confirmRes, { 'confirmed is 200': (r) => r.status === 200 });
  }

  sleep(2);
}

export function teardown(d) {
  const c = http.get(`${API}/classes/${d.classId}`).json();
  if (c.heldSeats + c.confirmedSeats > c.totalSeats) {
    throw new Error('OVERSOLD');
  }
}
