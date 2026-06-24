# Build script for servo-csharp-bindings
# Usage: .\build.ps1 [-Configuration Debug|Release] [-ServoDir <path>]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$ServoDir = "$PSScriptRoot\external\servo"
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot

Write-Host "=== Building servo-ffi ===" -ForegroundColor Cyan

$CargoProfile = if ($Configuration -eq "Release") { "--release" } else { "" }
$TargetSubdir = if ($Configuration -eq "Release") { "release" } else { "debug" }

# Keep servo-ffi's dependencies in sync with the servo submodule. servo-ffi is a
# separate Cargo workspace, so its Cargo.lock resolves independently and drifts
# from servo's after a submodule bump. Seed our lock from servo's when servo's is
# newer; the cargo build below then resolves only servo-ffi's extra crates,
# leaving the shared crates pinned to servo's versions.
$ServoLock = Join-Path $ServoDir "Cargo.lock"
$FfiLock = Join-Path $RootDir "servo-ffi\Cargo.lock"
if (Test-Path $ServoLock) {
    $servoMsrv = (Select-String -Path (Join-Path $ServoDir "Cargo.toml") -Pattern '^rust-version\s*=\s*"([^"]+)"' | Select-Object -First 1).Matches.Groups[1].Value
    $ffiMsrv = (Select-String -Path (Join-Path $RootDir "servo-ffi\Cargo.toml") -Pattern '^rust-version\s*=\s*"([^"]+)"' | Select-Object -First 1).Matches.Groups[1].Value
    if ($servoMsrv -and $servoMsrv -ne $ffiMsrv) {
        Write-Host "  Warning: servo-ffi rust-version ($ffiMsrv) != servo ($servoMsrv)." -ForegroundColor Yellow
        Write-Host "           Update servo-ffi/Cargo.toml's rust-version to match, or Cargo's" -ForegroundColor Yellow
        Write-Host "           MSRV-aware resolver may hold deps back to older versions than servo." -ForegroundColor Yellow
    }
    if ((-not (Test-Path $FfiLock)) -or ((Get-Item $ServoLock).LastWriteTime -gt (Get-Item $FfiLock).LastWriteTime)) {
        Copy-Item $ServoLock $FfiLock -Force
        Write-Host "  Synced servo-ffi/Cargo.lock from $ServoLock" -ForegroundColor Green
    }
}

Push-Location "$RootDir\servo-ffi"
try {
    $cmd = "cargo build $CargoProfile"
    Write-Host "Running: $cmd"
    Invoke-Expression $cmd
    if ($LASTEXITCODE -ne 0) {
        throw "cargo build failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

# Locate the cargo target directory
$TargetDir = "$RootDir\servo-ffi\target\$TargetSubdir"
$NativeLib = "$TargetDir\servo_ffi.dll"
if (-not (Test-Path $NativeLib)) {
    $TargetDir = "$ServoDir\target\$TargetSubdir"
    $NativeLib = "$TargetDir\servo_ffi.dll"
}
if (-not (Test-Path $NativeLib)) {
    throw "Could not find servo_ffi.dll in any expected target directory"
}

Write-Host ""
Write-Host "=== Copying native artifacts ===" -ForegroundColor Cyan

$ArtifactNativeDir = "$RootDir\artifacts\runtimes\win-x64\native"
New-Item -ItemType Directory -Force -Path $ArtifactNativeDir | Out-Null

# servo_ffi.dll
Copy-Item $NativeLib $ArtifactNativeDir -Force
Write-Host "  servo_ffi.dll" -ForegroundColor Green

# ANGLE DLLs
$AngleDlls = Get-ChildItem -Path "$TargetDir\build" -Recurse -Include "libEGL.dll","libGLESv2.dll" -ErrorAction SilentlyContinue
foreach ($dll in $AngleDlls) {
    Copy-Item $dll.FullName $ArtifactNativeDir -Force
    Write-Host "  $($dll.Name)" -ForegroundColor Green
}

# Servo resources
$ResourcesSource = "$ServoDir\resources"
$ResourcesDest = "$RootDir\artifacts\resources"
if (Test-Path $ResourcesSource) {
    if (Test-Path $ResourcesDest) { Remove-Item $ResourcesDest -Recurse -Force }
    Copy-Item $ResourcesSource $ResourcesDest -Recurse
    Write-Host "  resources/" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green