# ADR 0007: Standard Prometheus Metrics Exposition and Edge Metric Protection

## Status
Accepted

## Context
Prometheus scraping standards specify that metric exposition text formats include metric types, help strings, cumulative bucket counts (`_bucket{le="..."}`), sum (`_sum`), and count (`_count`) for histograms. Previously (Finding 4), the application registered `hold_duration_seconds` and `class_read_latency_seconds` histograms via .NET's `System.Diagnostics.Metrics.Histogram<double>`, but `/metrics` exported only basic counters, omitting the histogram series.

Additionally, in production architecture (Finding X5), the reverse proxy / edge container proxies all `/api/*` requests directly to backend services. Exposing internal metrics publicly over the Internet risks information leakage and endpoint abuse.

## Decision
1. **Histogram Exposition via Prometheus Standard Text Format:**
   `AppMetrics` implements a lock-synchronized, zero-dependency histogram tracker that records observations against standard bucket boundaries (`0.005`, `0.01`, `0.025`, `0.05`, `0.1`, `0.25`, `0.5`, `1`, `2.5`, `5`, `10`, `+Inf`).
   When `/metrics` is scraped, it formats both counters and full histogram structures (`_bucket`, `_sum`, `_count`) adhering strictly to the Prometheus exposition standard.
2. **Edge Proxy Metric Blocking:**
   In `edge/default.conf`, an exact match rule:
   ```nginx
   location = /api/metrics {
       return 404;
   }
   ```
   blocks public requests through the edge gateway with HTTP 404, while permitting internal scrapers (such as a cluster Prometheus agent) to scrape `backend:8080/metrics` directly over the isolated Docker bridge network.

## Consequences
- **Positive:** Full observability parity with standard Prometheus collectors without adding external NuGet packages. Latency and duration distributions are immediately inspectable. Public clients cannot inspect internal application metrics.
- **Negative:** Histograms use predefined bucket boundaries tailored to web request and hold duration latencies.
