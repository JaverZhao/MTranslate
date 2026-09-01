# llama.cpp runtime

Release builds place the regression-tested llama.cpp runtime in runtime-specific subdirectories:

- `win-x64`
- `win-arm64`
- `osx-arm64`
- `osx-x64`

Runtime binaries are not committed until the exact llama.cpp revision has passed the Hy-MT2 Q2_0c and Q4_K_M translation regression suite. The Phase 1 POC accepts the executable path explicitly through `run-server --exe` so an unverified `latest` binary cannot be selected implicitly.

The experimental Q2_0C runtime is isolated under `runtime/q2c-<rid>`. Windows x64 is currently pinned to PR 19357 commit `2af64dd00a6689a7bfaf69b4768a944d0ec6bade`; reproduce it with `eng/build-q2c-runtime.ps1`. MTranslate selects this runtime only for `hy-mt2-1.8b-2bit` and keeps the standard b10516 runtime for `hy-mt2-1.8b-q4`.

Windows x64 GPU inference uses the official b10516 Vulkan artifact under `runtime/win-vulkan-x64` and passes `--n-gpu-layers 999` for the Standard Q4 model. The Fast Q2_0C compatibility branch currently has no validated GPU backend, so explicit GPU mode excludes that model instead of silently running it on CPU.
