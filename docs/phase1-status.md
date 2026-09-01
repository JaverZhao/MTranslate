# Phase 1 inference status

Date: 2026-09-01

## Standard model: passed

Validated assets:

- Model: `tencent/Hy-MT2-1.8B-GGUF`, `Hy-MT2-1.8B-Q4_K_M.gguf`
- Model size: `1,133,080,448` bytes
- Model SHA256: `dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699`
- llama.cpp build: `b10516`
- llama.cpp commit: `b95502ba9aa0eb73a2f4fc8878d7fbe6a847a0b9`
- Runtime artifact SHA256: `fbbbc55e0eb2e1b07f9dcb9488616c98ed47d9003b90e15e7c8c7812c4307cd3`
- Platform: Windows 10 x64, CPU backend

Regression result:

| Case | Result | Observed output |
| --- | --- | --- |
| English to Chinese | Pass | 本地翻译可以保留电脑上的私人文本。 |
| Chinese to English | Pass | Local translation can protect user privacy. |
| SSE streaming | Pass | Received 11 non-empty chunks |
| HTTP cancellation | Pass | Cancellation observed in 31 ms |
| Three-run benchmark | Pass | 864 ms average, 22.77 completion tokens/s |

The exact run report is generated locally at `artifacts/phase1/q4-regression-report.json`. The artifacts directory is intentionally excluded from version control because reports contain machine-specific absolute paths.

### Windows x64 Vulkan GPU: passed

Validated on an NVIDIA GeForce RTX 3080 10 GB with the official b10516 Vulkan runtime. MTranslate passes `--n-gpu-layers 999`; llama.cpp reported `offloaded 33/33 layers to GPU` and assigned the model to `Vulkan0`. The EN/ZH, ZH/EN, SSE streaming, cancellation and benchmark regression all passed. Observed average latency was 947 ms and average completion throughput was 23.53 tokens/s. The local report is `artifacts/phase1/q4-vulkan-regression-report.json`.

GPU mode is intentionally limited to the Standard Q4 model. The experimental Q2_0C compatibility runtime has only been validated on CPU, so MTranslate rejects that combination explicitly rather than presenting a GPU selection while silently falling back to CPU.

## Fast model: experimental pass on Windows x64

Validated on 2026-08-31:

- Model: `tencent/Hy-MT2-1.8B-2Bit-GGUF`, `Hy-MT2-1.8B-2Bit.gguf`
- Model size: `600,534,880` bytes
- Model SHA256: `dcc33bbae9b28d923c8c76a64f6157840841d26f8774f3dfd770d5fabeeb1cd7`
- Compatibility runtime: llama.cpp PR 19357 head commit `2af64dd00a6689a7bfaf69b4768a944d0ec6bade`
- Platform: Windows 10 x64, generic CPU backend

The official GGUF loaded successfully without tensor offset errors. The full Phase 1 regression passed: EN to ZH, ZH to EN, SSE streaming, HTTP cancellation, and the three-run benchmark. The benchmark averaged 2,446 ms and 8.37 completion tokens/s on the validation machine; the machine-readable report is `artifacts/phase1/q2c-regression-report.json`.

This remains an experimental path because Q2_0C is not part of the standard llama.cpp release. MTranslate therefore keeps two isolated runtimes: the pinned b10516 runtime for Q4_K_M, and a pinned PR runtime for Q2_0C. Switching back to the Standard model never depends on the experimental runtime.

Remaining release gates:

1. Run the document-format regression suite against Q2_0C.
2. Build and validate Windows arm64 and macOS arm64 compatibility runtimes.
3. Replace the PR runtime with an official llama.cpp release when Q2_0C reaches upstream.
