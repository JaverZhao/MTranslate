# Phase 2 Core status

Date: 2026-08-27

## Implemented

The Core milestone now includes all modules required by section 72 of the development specification:

| Module | Implementation |
| --- | --- |
| TranslationService | Unified queued translation, chunk context, result merging, token totals, and cache reuse |
| PromptBuilder | Source, target, and previous-context prompt construction with profile validation |
| ChunkManager | Line-ending and Unicode normalization; paragraph, line, sentence, and whitespace boundaries; configurable token targets |
| Cache | SQLite schema version 1, SHA256 cache identity, hit tracking, disable, clear, and least-recently-used trimming |
| JobQueue | High, normal, and low priorities; one or two workers; cancellation; pause and drain for model switching |
| ModelManager | Catalog registration, SHA256 verification, explicit state transitions, and single active model |
| RuntimeManager | Process interruption notification, automatic recovery, and a three-crashes-in-five-minutes circuit breaker |

The existing `LlamaServerRuntime` implements the Core runtime contract through `LlamaRuntimeFactory`. Model switching therefore stops and disposes the old llama-server before starting the selected model.

## Acceptance

Automated coverage includes:

- chunk boundary selection, whitespace preservation, Unicode normalization, and oversized-word handling;
- queue priority, pause and drain, and cancellation;
- multi-chunk translation, previous-segment context, and repeat-request cache hits;
- model checksum validation and ready-state transitions;
- runtime replacement, unexpected-exit restart, and crash circuit breaking;
- SQLite persistence, hit counts, disabled mode, and clearing;
- all Phase 1 prompt, HTTP, streaming, runtime, and downloader regressions.

Release verification result:

| Check | Result |
| --- | --- |
| Build | Pass, 0 warnings and 0 errors |
| Core tests | Pass, 17 of 17 |
| Infrastructure tests | Pass, 11 of 11 |
| English to Chinese runtime regression | Pass, 617 ms |
| Chinese to English runtime regression | Pass, 399 ms |
| SSE streaming runtime regression | Pass, 14 chunks |
| Cancellation runtime regression | Pass, observed in 47 ms |
| Three-run benchmark | Pass, 923 ms average and 21.32 completion tokens per second |

The machine-readable runtime report is generated locally at `artifacts/phase2/runtime-regression-report.json`.

Phase 3 desktop UI can now consume the Core interfaces without directly controlling llama-server or storing translation cache state in views.
