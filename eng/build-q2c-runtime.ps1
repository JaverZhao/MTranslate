[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string] $Rid = 'win-x64',
    [string] $SourceDirectory = (Join-Path $PSScriptRoot '..\artifacts\q2c-runtime-source'),
    [string] $BuildDirectory = (Join-Path $PSScriptRoot '..\artifacts\q2c-runtime-build'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\runtime\q2c-$Rid")
)

$ErrorActionPreference = 'Stop'
$commit = '2af64dd00a6689a7bfaf69b4768a944d0ec6bade'
$repository = 'https://github.com/chaxu01/llama.cpp.git'

function Invoke-Checked {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Command)
    $executable = $Command[0]
    $arguments = $Command[1..($Command.Length - 1)]
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $($Command -join ' ')"
    }
}

$sourcePath = [System.IO.Path]::GetFullPath($SourceDirectory)
$buildPath = [System.IO.Path]::GetFullPath($BuildDirectory)
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path (Join-Path $sourcePath '.git'))) {
    New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null
    Invoke-Checked git -C $sourcePath init
    Invoke-Checked git -C $sourcePath remote add origin $repository
}

Invoke-Checked git -C $sourcePath fetch --depth 1 origin $commit
Invoke-Checked git -C $sourcePath checkout --detach $commit
Invoke-Checked cmake -S $sourcePath -B $buildPath -G 'Visual Studio 17 2022' -A x64 `
    -DGGML_NATIVE=OFF `
    -DGGML_CPU_ALL_VARIANTS=OFF `
    -DLLAMA_CURL=OFF `
    -DLLAMA_BUILD_TESTS=OFF `
    -DLLAMA_BUILD_EXAMPLES=OFF `
    -DLLAMA_BUILD_SERVER=ON
Invoke-Checked cmake --build $buildPath --config Release --target llama-server -j 2

$binaryPath = Join-Path $buildPath 'bin\Release'
$files = @(
    'ggml-base.dll',
    'ggml-cpu.dll',
    'ggml.dll',
    'llama-common.dll',
    'llama-server-impl.dll',
    'llama-server.exe',
    'llama.dll',
    'mtmd.dll'
)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $binaryPath $file) -Destination $outputPath -Force
}

Write-Output "Q2_0C runtime built from llama.cpp commit $commit"
Get-ChildItem -LiteralPath $outputPath -File | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [PSCustomObject]@{ File = $_.Name; Size = $_.Length; Sha256 = $hash }
}
