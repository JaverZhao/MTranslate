# Phase 5 Local API status

Date: 2026-08-28

## Gateway and endpoints

The desktop application starts an ASP.NET Core Minimal API Gateway bound only to `127.0.0.1`. It selects the first available port from `17891`, `17893`, `17895`, `17897`, and `17899`; the actual base URL is shown on the Local API page.

| Endpoint | Authentication | Behavior |
| --- | --- | --- |
| `GET /api/v1/health` | Anonymous | API, engine, active-model, and load status |
| `POST /api/v1/pair` | One-time code | Issues one client-specific bearer token |
| `GET /api/v1/info` | Bearer | API capabilities and all 38 supported languages |
| `GET /api/v1/models` | Bearer | Active and installed model information |
| `POST /api/v1/translate` | Bearer | Single text translation with context, cache choice, and line preservation |
| `POST /api/v1/translate/batch` | Bearer | Up to 50 uniquely identified items |
| `POST /api/v1/translate/stream` | Bearer | Stable `start`, `delta`, `complete`, and `error` SSE events |

The Gateway uses the same model lifecycle, language detector, priority queue, translation cache, and crash recovery path as the desktop translator. It never exposes llama-server's internal port or API key.

## Pairing and client management

- The desktop page generates a cryptographically random six-digit code with a five-minute lifetime.
- A code is invalidated immediately after one successful pairing.
- Each client receives an independent 256-bit Base64Url token.
- SQLite stores only the token's SHA-256 hash.
- Client name, type, creation time, last-used time, permissions, and revocation state are persisted.
- The Local API page lists clients and can revoke each token.

## Local security

- Kestrel listens on IPv4 loopback only and does not emit its server header.
- Only `127.0.0.1` and `localhost` Host headers are accepted.
- Browser requests are accepted only from `chrome-extension://` and `moz-extension://` origins; arbitrary web origins are rejected and wildcard CORS is never returned.
- Anonymous access is limited to health, one-time pairing, and extension preflight.
- Normal clients are limited to 120 authenticated requests per minute; batch requests are limited to 30 per minute.
- Pairing attempts are independently limited to 10 per minute.
- Single request text is limited to approximately 32 KB; batch count, UTF-8 size, and estimated tokens are bounded.
- Error responses use stable problem JSON and do not expose internal exceptions for unexpected failures.

## Desktop integration

The Local API page now shows live online/offline status, the selected endpoint, a one-time pairing-code workflow, active client count, last-used timestamps, and token revocation. Gateway startup and shutdown follow the Avalonia application lifetime.

## Acceptance

| Check | Result |
| --- | --- |
| Release build | Pass, 0 warnings and 0 errors |
| Release full test suite | Pass, 110 of 110 |
| Live desktop Gateway health | Pass, `127.0.0.1:17891`, API `1.0` |
| Desktop/Gateway shutdown cleanup | Pass; no desktop or llama-server process remained |
| Health is anonymous | Pass |
| Protected endpoints reject missing tokens | Pass, HTTP 401 |
| Pairing token length | Pass, 256-bit / 43-character Base64Url |
| Plaintext token absent from SQLite | Pass |
| Revoked token rejected | Pass |
| Arbitrary HTTPS Origin rejected | Pass, HTTP 403 |
| Chrome extension Origin echoed explicitly | Pass |
| Invalid Host rejected | Pass, HTTP 400 |
| Per-client rate limiting | Pass, HTTP 429 with `Retry-After` |
| 32 KB input limit | Pass |
| Stable SSE protocol | Pass |

## Next phase boundary

Phase 6 can implement the Chrome/Edge Manifest V3 browser extension against this stable Gateway contract. Browser integration must use pairing and `/api/v1/translate`; it must never call llama-server directly.
