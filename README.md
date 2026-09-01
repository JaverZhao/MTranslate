# MTranslate

MTranslate is a local-first Windows and macOS desktop translator built around Hy-MT2 GGUF models and llama.cpp. Development follows the phases in `MTranslate_Desktop_Translator_Development_Spec.md`.

## Current milestone

Phase 1 inference POC, Phase 2 Core, Phase 3 Avalonia desktop UI, Phase 4 document translation, and Phase 5 Local API are complete. The repository now provides:

- the specification prompt format and configurable inference profile;
- a llama-server OpenAI-compatible HTTP client;
- normal and server-sent-event streaming translation;
- cancellation propagation;
- managed llama-server startup, health checking, logging, and shutdown;
- resumable HTTP model download with SHA256 verification and atomic installation;
- a benchmark command that reports latency and tokens per second when usage data is available;
- a complete Phase 1 regression command covering EN to ZH, ZH to EN, streaming, cancellation, and benchmarks;
- an authenticated internal llama-server with an automatically generated 256-bit API key;
- pinned model and runtime manifests with source URLs, sizes, revisions, and SHA256 values;
- xUnit coverage for prompts, HTTP responses, streaming, failures, and downloads.
- a unified priority translation queue with one or two inference slots, pause, drain, and cancellation;
- paragraph, line, sentence, and token-aware long-text chunking without splitting Latin words;
- a translation orchestration service with per-chunk context, merging, and cache reuse;
- a versioned SQLite translation cache with SHA256 keys, hit tracking, disable, clear, and size trimming;
- single-model switching with checksum verification and explicit model states;
- runtime crash monitoring, one-attempt recovery, and a three-crashes-in-five-minutes circuit breaker.
- an Avalonia 12 MVVM desktop application with Home, Files, History, Models, Local API, and Settings navigation;
- a functional Home translation workflow connected to model verification, llama-server, the priority queue, chunking, and SQLite cache;
- local copy, clear, language swap, cancellation, character counts, timing, and `Ctrl+Enter` translation;
- an ASP.NET Core loopback API Gateway with health, pairing, info, models, translation, batch, and SSE endpoints;
- per-client 256-bit bearer tokens stored only as SHA-256 hashes, revocation, extension-only CORS, Host checks, request limits, and rate limits;
- structure-preserving TXT, SRT, VTT, Markdown, and ASS parsers;
- byte-order-mark and line-ending round trips, subtitle batch translation, bilingual subtitle modes, token-weighted progress, checkpoints, and atomic output;
- a functional Files workspace with drag and drop, multi-file queues, language and output selection, pause, resume, retry, and output-folder access.

The repository does not commit model weights or llama.cpp binaries. Supply an explicitly selected and regression-tested llama-server executable and a GGUF model when running the POC or desktop translator. See `docs/phase3-status.md` for the desktop acceptance record.

## Local API

The desktop application starts the Gateway on the first available reserved loopback port: `17891`, `17893`, `17895`, `17897`, or `17899`. Open **本地 API**, generate a six-digit one-time code, then pair a client:

```powershell
$pair = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:17891/api/v1/pair" -ContentType "application/json" -Body '{"code":"123456","clientName":"My Tool","clientType":"desktop"}'
$headers = @{ Authorization = "Bearer $($pair.token)" }
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:17891/api/v1/translate" -Headers $headers -ContentType "application/json" -Body '{"text":"Hello world.","sourceLanguage":"en","targetLanguage":"zh-CN","mode":"standard"}'
```

Use the actual endpoint shown in the application. The six-digit code is illustrative; tokens are shown only once during pairing. See `docs/phase5-status.md` for the complete endpoint and security acceptance record.

## Build and test

```powershell
dotnet build MTranslate.slnx
dotnet test MTranslate.slnx --no-build
```

Run the desktop application:

```powershell
dotnet run --project src/MTranslate.Desktop
```

Translate a supported document through an already running llama-server:

```powershell
dotnet run --project src/MTranslate.Poc -- translate-file --server "http://127.0.0.1:17892" --input "input.srt" --output "input.zh-CN.srt" --source en --target zh-CN --api-key "YOUR_INTERNAL_KEY"
```

## POC commands

Show command help:

```powershell
dotnet run --project src/MTranslate.Poc -- help
```

Download a model with resumable HTTP Range support and mandatory SHA256 verification:

```powershell
dotnet run --project src/MTranslate.Poc -- download-model --url "https://model-host/model.gguf" --sha256 "64_HEXADECIMAL_CHARACTERS" --output "models/model.gguf"
```

Start llama-server and keep its lifetime attached to the POC process:

```powershell
dotnet run --project src/MTranslate.Poc -- run-server --exe "runtime/win-x64/llama-server.exe" --model "models/model.gguf"
```

Translate text using a running llama-server:

```powershell
dotnet run --project src/MTranslate.Poc -- translate --server "http://127.0.0.1:17892" --source English --target Chinese --text "Hello, world."
```

Stream translated output:

```powershell
dotnet run --project src/MTranslate.Poc -- translate --server "http://127.0.0.1:17892" --target Chinese --text "Hello, world." --stream
```

Benchmark three translation runs:

```powershell
dotnet run --project src/MTranslate.Poc -- benchmark --server "http://127.0.0.1:17892" --source English --target Chinese --text "Hello, world." --iterations 3
```

Run the complete Phase 1 regression suite while managing llama-server automatically:

```powershell
dotnet run --project src/MTranslate.Poc -- verify --exe "runtime/win-x64/llama-server.exe" --model "models/Hy-MT2-1.8B-Q4_K_M.gguf" --mode "standard-q4-k-m" --report "artifacts/phase1/q4-regression-report.json"
```

Windows x64 Vulkan GPU verification for the Standard Q4 model:

```powershell
dotnet run --project src/MTranslate.Poc -- verify --exe "runtime/win-vulkan-x64/llama-server.exe" --model "models/Hy-MT2-1.8B-Q4_K_M.gguf" --mode "standard-q4-k-m-vulkan" --gpu-layers 999 --report "artifacts/phase1/q4-vulkan-regression-report.json"
```

## Compatibility status

The standard Q4_K_M model is verified on Windows x64 with pinned llama.cpp build b10516. The Fast Q2_0c model is not release-ready because its required llama.cpp PR 19357 remains unmerged and the published GGUF has unresolved tensor offset mismatch reports on standard llama.cpp. See `docs/phase1-status.md` for the evidence and current gate.
