import http from 'k6/http';
import { check, sleep } from 'k6';

const API = __ENV.API || 'http://localhost:8080';

export const options = {
  scenarios: {
    steady_soak: {
      executor: 'constant-arrival-rate',
      rate: 100,
      timeUnit: '1s',
      duration: '60s', // 1 minute soak for verification (extendable manually)
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    http_req_duration: ['p(95)<1000'],
  },
};

export default function () {
  const res = http.get(`${API}/classes`);
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(0.5);
}
