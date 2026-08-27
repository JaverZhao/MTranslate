# Phase 1 inference status

Date: 2026-08-27

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

## Fast model: blocked by upstream compatibility

The requested Fast model uses the Q2_0c format. Its model card states that it depends on llama.cpp PR 19357. As of this validation date, that PR is still a draft and Q2_0c is not part of the standard llama.cpp release. The published Tencent GGUF also has unresolved reports of a tensor offset mismatch when loaded by standard llama.cpp builds.

The Fast model must not be marked installable until all of these gates pass:

1. A runtime branch or upstream release is selected by immutable commit.
2. The runtime supports the target CPU architectures, including Windows x64 and macOS arm64.
3. The published GGUF loads without tensor offset errors.
4. EN to ZH, ZH to EN, streaming, cancellation, and benchmark regression pass.
5. Runtime binaries and model files have pinned SHA256 values.

Current catalog behavior records the model and its official hash but marks runtime compatibility as `blocked-upstream`.
