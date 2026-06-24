#!/usr/bin/env bash
# Usage: ./build-android.sh [debug|release] [--servo-dir <path>] [--arch <arch>] [--ndk <path>]
#
# Builds servo-ffi for Android targets.
#
# Supported architectures: arm64 (default), arm, x86, x86_64
# You can specify multiple architectures: --arch arm64 --arch x86_64
#
# Requires:
#   - Android NDK (auto-detected from ANDROID_NDK_HOME, ANDROID_HOME/ndk/*, or ~/Library/Android/sdk/ndk/*)
#   - Rust targets: rustup target add aarch64-linux-android armv7-linux-androideabi i686-linux-android x86_64-linux-android
#

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIGURATION="debug"
SERVO_DIR=""
NDK_PATH=""
ARCHS=()
API_LEVEL=30

while [[ $# -gt 0 ]]; do
    case "$1" in
        --servo-dir) SERVO_DIR="$2"; shift 2 ;;
        --ndk) NDK_PATH="$2"; shift 2 ;;
        --arch) ARCHS+=("$2"); shift 2 ;;
        --api) API_LEVEL="$2"; shift 2 ;;
        release|Release) CONFIGURATION="release"; shift ;;
        debug|Debug) CONFIGURATION="debug"; shift ;;
        *) shift ;;
    esac
done

# Default to arm64 if no arch specified
if [ ${#ARCHS[@]} -eq 0 ]; then
    ARCHS=("arm64")
fi

# --- Locate the Android NDK ---
find_ndk() {
    if [ -n "$NDK_PATH" ]; then
        echo "$NDK_PATH"
        return
    fi
    if [ -n "${ANDROID_NDK_HOME:-}" ] && [ -d "$ANDROID_NDK_HOME" ]; then
        echo "$ANDROID_NDK_HOME"
        return
    fi
    # Search ANDROID_HOME/ndk/ or default SDK location
    local preferred_ndk="28.0.13004108"
    local sdk_dirs=("${ANDROID_HOME:-}" "$HOME/Library/Android/sdk" "$HOME/Android/Sdk")
    for sdk in "${sdk_dirs[@]}"; do
        if [ -n "$sdk" ] && [ -d "$sdk/ndk/$preferred_ndk" ]; then
            echo "$sdk/ndk/$preferred_ndk"
            return
        fi
    done
    # Fall back to latest installed NDK
    for sdk in "${sdk_dirs[@]}"; do
        if [ -n "$sdk" ] && [ -d "$sdk/ndk" ]; then
            local latest
            latest="$(ls -1 "$sdk/ndk" | sort -V | tail -1)"
            if [ -n "$latest" ]; then
                echo "$sdk/ndk/$latest"
                return
            fi
        fi
    done
    return 1
}

NDK_PATH="$(find_ndk)" || { echo "Error: Android NDK not found. Set ANDROID_NDK_HOME or pass --ndk <path>."; exit 1; }
echo "Using NDK: $NDK_PATH"

# Verify NDK has the toolchain
NDK_TOOLCHAIN="$NDK_PATH/toolchains/llvm/prebuilt"
HOST_TAG=""
case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) HOST_TAG="darwin-x86_64" ;;
    Darwin-x86_64) HOST_TAG="darwin-x86_64" ;;
    Linux-x86_64) HOST_TAG="linux-x86_64" ;;
    *) echo "Unsupported host for Android NDK cross-compilation"; exit 1 ;;
esac

NDK_TOOLCHAIN_BIN="$NDK_TOOLCHAIN/$HOST_TAG/bin"
if [ ! -d "$NDK_TOOLCHAIN_BIN" ]; then
    echo "Error: NDK toolchain not found at $NDK_TOOLCHAIN_BIN"
    exit 1
fi

# Map arch names to Rust targets, NDK compiler prefixes, and .NET RIDs
arch_to_rust_target() {
    case "$1" in
        arm64|aarch64) echo "aarch64-linux-android" ;;
        arm|armv7) echo "armv7-linux-androideabi" ;;
        x86|i686) echo "i686-linux-android" ;;
        x86_64) echo "x86_64-linux-android" ;;
        *) echo "Unknown arch: $1" >&2; return 1 ;;
    esac
}

arch_to_clang_prefix() {
    case "$1" in
        arm64|aarch64) echo "aarch64-linux-android${API_LEVEL}" ;;
        arm|armv7) echo "armv7a-linux-androideabi${API_LEVEL}" ;;
        x86|i686) echo "i686-linux-android${API_LEVEL}" ;;
        x86_64) echo "x86_64-linux-android${API_LEVEL}" ;;
        *) return 1 ;;
    esac
}

arch_to_rid() {
    case "$1" in
        arm64|aarch64) echo "android-arm64" ;;
        arm|armv7) echo "android-arm" ;;
        x86|i686) echo "android-x86" ;;
        x86_64) echo "android-x64" ;;
        *) return 1 ;;
    esac
}

# Ensure Rust targets are installed
for arch in "${ARCHS[@]}"; do
    target="$(arch_to_rust_target "$arch")"
    if ! rustup target list --installed | grep -q "^${target}$"; then
        echo "Installing Rust target: $target"
        rustup target add "$target"
    fi
done

# --- Build for each architecture ---
cd "$SCRIPT_DIR/servo-ffi"

CARGO_ARGS=""
if [ "$CONFIGURATION" = "release" ]; then
    CARGO_ARGS="--release"
fi

for arch in "${ARCHS[@]}"; do
    RUST_TARGET="$(arch_to_rust_target "$arch")"
    CLANG_PREFIX="$(arch_to_clang_prefix "$arch")"
    RID="$(arch_to_rid "$arch")"

    echo ""
    echo "=== Building servo-ffi for $arch ($RUST_TARGET) ==="

    # Set up the NDK toolchain for this target
    export PATH="$NDK_TOOLCHAIN_BIN:$PATH"
    TARGET_UNDERSCORE="$(echo "$RUST_TARGET" | tr '-' '_')"
    TARGET_UPPER="$(echo "$TARGET_UNDERSCORE" | tr '[:lower:]' '[:upper:]')"

    eval "export CC_${TARGET_UNDERSCORE}=\"${NDK_TOOLCHAIN_BIN}/${CLANG_PREFIX}-clang\""
    eval "export CXX_${TARGET_UNDERSCORE}=\"${NDK_TOOLCHAIN_BIN}/${CLANG_PREFIX}-clang++\""
    eval "export AR_${TARGET_UNDERSCORE}=\"${NDK_TOOLCHAIN_BIN}/llvm-ar\""
    eval "export RANLIB_${TARGET_UNDERSCORE}=\"${NDK_TOOLCHAIN_BIN}/llvm-ranlib\""

    # Also set bare AR/RANLIB for autoconf-based builds (e.g., jemalloc)
    # that don't use the cc crate's target-specific env vars
    export AR="${NDK_TOOLCHAIN_BIN}/llvm-ar"
    export RANLIB="${NDK_TOOLCHAIN_BIN}/llvm-ranlib"

    # Configure jemalloc for 16KB page size (Android 15+ devices)
    export JEMALLOC_SYS_WITH_LG_PAGE=14

    # Cargo linker config via env (overrides .cargo/config.toml)
    eval "export CARGO_TARGET_${TARGET_UPPER}_LINKER=\"${NDK_TOOLCHAIN_BIN}/${CLANG_PREFIX}-clang\""

    # Sysroot and target flags for C/C++ compilation and bindgen header parsing
    NDK_SYSROOT="$NDK_TOOLCHAIN/$HOST_TAG/sysroot"
    CLANG_TARGET_FLAGS="--target=${CLANG_PREFIX} --sysroot=${NDK_SYSROOT} -D__ANDROID_API__=${API_LEVEL}"

    eval "export CFLAGS_${TARGET_UNDERSCORE}=\"${CLANG_TARGET_FLAGS}\""
    eval "export CXXFLAGS_${TARGET_UNDERSCORE}=\"${CLANG_TARGET_FLAGS}\""
    eval "export BINDGEN_EXTRA_CLANG_ARGS_${TARGET_UNDERSCORE}=\"${CLANG_TARGET_FLAGS}\""

    export LIBCLANG_PATH="$NDK_TOOLCHAIN/$HOST_TAG/lib"

    # CMake toolchain for native dependencies that use cmake-rs
    export CMAKE_TOOLCHAIN_FILE="$NDK_PATH/build/cmake/android.toolchain.cmake"
    export ANDROID_ABI
    case "$arch" in
        arm64|aarch64) ANDROID_ABI="arm64-v8a" ;;
        arm|armv7) ANDROID_ABI="armeabi-v7a" ;;
        x86|i686) ANDROID_ABI="x86" ;;
        x86_64) ANDROID_ABI="x86_64" ;;
    esac
    export ANDROID_PLATFORM="android-${API_LEVEL}"
    export ANDROID_NDK="$NDK_PATH"
    export ANDROID_NDK_HOME="$NDK_PATH"
    export NDK_CMAKE_TOOLCHAIN_FILE="$CMAKE_TOOLCHAIN_FILE"

    cargo build $CARGO_ARGS --target "$RUST_TARGET"

    # Copy output to artifacts
    TARGET_DIR="$SCRIPT_DIR/servo-ffi/target/$RUST_TARGET/$CONFIGURATION"
    NATIVE_DIR="$SCRIPT_DIR/artifacts/runtimes/$RID/native"
    mkdir -p "$NATIVE_DIR"

    NATIVE_LIB="$TARGET_DIR/libservo_ffi.so"
    if [ -f "$NATIVE_LIB" ]; then
        cp "$NATIVE_LIB" "$NATIVE_DIR/"
        echo "  libservo_ffi.so → $RID"
    else
        echo "  Warning: $NATIVE_LIB not found"
    fi

    # Copy libc++_shared.so (required runtime dependency)
    NDK_SYSROOT="$NDK_TOOLCHAIN/$HOST_TAG/sysroot"
    LIBCXX=""
    case "$arch" in
        arm64|aarch64) LIBCXX="$NDK_SYSROOT/usr/lib/aarch64-linux-android/libc++_shared.so" ;;
        arm|armv7) LIBCXX="$NDK_SYSROOT/usr/lib/arm-linux-androideabi/libc++_shared.so" ;;
        x86|i686) LIBCXX="$NDK_SYSROOT/usr/lib/i686-linux-android/libc++_shared.so" ;;
        x86_64) LIBCXX="$NDK_SYSROOT/usr/lib/x86_64-linux-android/libc++_shared.so" ;;
    esac
    if [ -n "$LIBCXX" ] && [ -f "$LIBCXX" ]; then
        cp "$LIBCXX" "$NATIVE_DIR/"
        echo "  libc++_shared.so → $RID"
    fi
done

# Copy Servo resources (shared across all architectures) if a local servo
# checkout was supplied. The crates.io servo package does not ship resources/,
# so this step is opt-in via --servo-dir.
if [ -n "$SERVO_DIR" ]; then
    RESOURCES_SRC="$SERVO_DIR/resources"
    RESOURCES_DST="$SCRIPT_DIR/artifacts/resources"
    if [ -d "$RESOURCES_SRC" ]; then
        rm -rf "$RESOURCES_DST"
        cp -r "$RESOURCES_SRC" "$RESOURCES_DST"
        echo ""
        echo "  resources/ copied from $SERVO_DIR"
    else
        echo ""
        echo "  Warning: --servo-dir set but $RESOURCES_SRC not found; skipping resources copy"
    fi
else
    echo ""
    echo "  resources/ skipped (pass --servo-dir <path-to-servo-checkout> to copy)"
fi

echo ""
echo "Android build complete."
echo "Artifacts:"
for arch in "${ARCHS[@]}"; do
    RID="$(arch_to_rid "$arch")"
    echo "  artifacts/runtimes/$RID/native/libservo_ffi.so"
done
