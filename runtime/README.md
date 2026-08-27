# llama.cpp runtime

Release builds place the regression-tested llama.cpp runtime in runtime-specific subdirectories:

- `win-x64`
- `win-arm64`
- `osx-arm64`
- `osx-x64`

Runtime binaries are not committed until the exact llama.cpp revision has passed the Hy-MT2 Q2_0c and Q4_K_M translation regression suite. The Phase 1 POC accepts the executable path explicitly through `run-server --exe` so an unverified `latest` binary cannot be selected implicitly.
