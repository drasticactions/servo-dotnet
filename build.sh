#!/usr/bin/env bash
# Usage: ./build.sh [debug|release] [--servo-dir <path>] [--target <rust-target>] [--gstreamer]
#

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIGURATION="debug"
SERVO_DIR="${SCRIPT_DIR}/external/servo"
RUST_TARGET=""
ENABLE_GSTREAMER=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --servo-dir) SERVO_DIR="$2"; shift 2 ;;
        --target) RUST_TARGET="$2"; shift 2 ;;
        --gstreamer) ENABLE_GSTREAMER=true; shift ;;
        release|Release) CONFIGURATION="release"; shift ;;
        debug|Debug) CONFIGURATION="debug"; shift ;;
        *) shift ;;
    esac
done

# Normalize SERVO_DIR to an absolute path: it may be passed relative (e.g.
# --servo-dir external/servo), but the script changes directories before using
# it for the dependency sync and the resources copy.
if [ -d "$SERVO_DIR" ]; then
    SERVO_DIR="$(cd "$SERVO_DIR" && pwd)"
fi

echo "Building servo-ffi"

CARGO_ARGS=""
if [ "$CONFIGURATION" = "release" ]; then
    CARGO_ARGS="--release"
fi
if [ -n "$RUST_TARGET" ]; then
    CARGO_ARGS="$CARGO_ARGS --target $RUST_TARGET"
fi

cd "$SCRIPT_DIR/servo-ffi"

# Set up GStreamer pkg-config paths for media-gstreamer feature
if [ "$ENABLE_GSTREAMER" = true ] && [ "$(uname -s)" = "Darwin" ] && [ -d "/Library/Frameworks/GStreamer.framework/Versions/1.0" ]; then
    GST_ROOT="/Library/Frameworks/GStreamer.framework/Versions/1.0"
    export PKG_CONFIG_PATH="${GST_ROOT}/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
    export PATH="${GST_ROOT}/bin${PATH:+:$PATH}"
fi

if [ "$ENABLE_GSTREAMER" = true ]; then
    CARGO_ARGS="$CARGO_ARGS --features servo/media-gstreamer"
fi

# --- Keep servo-ffi's dependencies in sync with the servo submodule ---
# servo-ffi is a separate Cargo workspace, so its Cargo.lock resolves
# independently and drifts from servo's after a submodule bump (e.g. a shared
# transitive crate landing on a different patch). Seed our lock from servo's
# whenever servo's is newer; the `cargo build` below then resolves only the
# extra crates servo-ffi adds on top, leaving the shared crates pinned to
# servo's versions.
SERVO_LOCK="$SERVO_DIR/Cargo.lock"
FFI_LOCK="$SCRIPT_DIR/servo-ffi/Cargo.lock"
if [ -f "$SERVO_LOCK" ]; then
    SERVO_MSRV="$(sed -nE 's/^rust-version[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "$SERVO_DIR/Cargo.toml" | head -1)"
    FFI_MSRV="$(sed -nE 's/^rust-version[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "$SCRIPT_DIR/servo-ffi/Cargo.toml" | head -1)"
    if [ -n "$SERVO_MSRV" ] && [ "$SERVO_MSRV" != "$FFI_MSRV" ]; then
        echo "  Warning: servo-ffi rust-version ($FFI_MSRV) != servo ($SERVO_MSRV)."
        echo "           Update servo-ffi/Cargo.toml's rust-version to match, or Cargo's"
        echo "           MSRV-aware resolver may hold deps back to older versions than servo."
    fi
    if [ "$SERVO_LOCK" -nt "$FFI_LOCK" ]; then
        cp "$SERVO_LOCK" "$FFI_LOCK"
        echo "  Synced servo-ffi/Cargo.lock from $SERVO_LOCK"
    fi
fi

cargo build $CARGO_ARGS

TARGET_SUBDIR="$CONFIGURATION"
if [ -n "$RUST_TARGET" ]; then
    TARGET_DIR="$SCRIPT_DIR/servo-ffi/target/$RUST_TARGET/$TARGET_SUBDIR"
else
    TARGET_DIR="$SCRIPT_DIR/servo-ffi/target/$TARGET_SUBDIR"
fi

# Determine the RID and library extension
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS" in
    Linux*)
        RID="linux-x64"
        LIB_EXT="so"
        LIB_PREFIX="lib"
        ;;
    Darwin*)
        if [ "$ARCH" = "arm64" ]; then
            RID="osx-arm64"
        else
            RID="osx-x64"
        fi
        LIB_EXT="dylib"
        LIB_PREFIX="lib"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        RID="win-x64"
        LIB_EXT="dll"
        LIB_PREFIX=""
        ;;
    *)
        echo "Unknown OS: $OS"
        exit 1
        ;;
esac

# Copy native libraries to the artifact directory
NATIVE_DIR="$SCRIPT_DIR/artifacts/runtimes/$RID/native"
mkdir -p "$NATIVE_DIR"

NATIVE_LIB="$TARGET_DIR/${LIB_PREFIX}servo_ffi.$LIB_EXT"
if [ -f "$NATIVE_LIB" ]; then
    cp "$NATIVE_LIB" "$NATIVE_DIR/"
    echo "  ${LIB_PREFIX}servo_ffi.$LIB_EXT → $RID"
fi

# Windows: also copy ANGLE DLLs
if [ "$RID" = "win-x64" ]; then
    for dll in libEGL.dll libGLESv2.dll; do
        found=$(find "$TARGET_DIR/build" -name "$dll" 2>/dev/null | head -1)
        if [ -n "$found" ]; then
            cp "$found" "$NATIVE_DIR/"
            echo "  $dll → $RID"
        fi
    done
fi

if [ -n "$SERVO_DIR" ]; then
    RESOURCES_SRC="$SERVO_DIR/resources"
    RESOURCES_DST="$SCRIPT_DIR/artifacts/resources"
    if [ -d "$RESOURCES_SRC" ]; then
        rm -rf "$RESOURCES_DST"
        cp -r "$RESOURCES_SRC" "$RESOURCES_DST"
        echo "  resources/ copied from $SERVO_DIR"
    else
        echo "  Warning: --servo-dir set but $RESOURCES_SRC not found; skipping resources copy"
    fi
else
    echo "  resources/ skipped (pass --servo-dir <path-to-servo-checkout> to copy)"
fi

# Copy GStreamer libraries for media playback (macOS)
if [ "$ENABLE_GSTREAMER" = true ] && [ "$(uname -s)" = "Darwin" ] && [ -d "${GST_ROOT:-}/lib" ]; then
    GSTREAMER_DST="$SCRIPT_DIR/artifacts/lib"
    rm -rf "$GSTREAMER_DST"
    mkdir -p "$GSTREAMER_DST"

    GST_PLUGIN_DIR="$GST_ROOT/lib/gstreamer-1.0"
    GST_LIB_DIR="$GST_ROOT/lib"

    # Plugin list matching Servo's gstreamer_plugin_lists/{common,macos}.rs.in
    GST_PLUGINS=(
        # common
        gstcoreelements gstnice gstapp gstaudioconvert gstaudioresample gstgio gstogg
        gstopengl gstopus gstplayback gsttheora gsttypefindfunctions gstvideoconvertscale
        gstvolume gstvorbis gstaudiofx gstaudioparsers gstautodetect gstdeinterlace
        gstid3demux gstinterleave gstisomp4 gstmatroska gstrtp gstrtpmanager
        gstvideofilter gstvpx gstwavparse gstaudiobuffersplit gstdtls gstid3tag
        gstproxy gstvideoparsersbad gstwebrtc gstlibav
        # macos
        gstosxaudio gstosxvideo gstapplemedia
    )

    # Copy plugins
    for plugin in "${GST_PLUGINS[@]}"; do
        src="$GST_PLUGIN_DIR/lib${plugin}.dylib"
        if [ -f "$src" ]; then
            cp "$src" "$GSTREAMER_DST/"
        fi
    done

    # Resolve an @rpath reference to an actual file in the GStreamer framework.
    # Handles both @rpath/libfoo.dylib and @rpath/lib/libfoo.dylib patterns.
    resolve_rpath_dep() {
        local dep="$1"
        local basename="${dep#@rpath/}"
        # Strip leading "lib/" if present (framework internal layout)
        local flat="${basename#lib/}"
        for search_dir in "$GST_LIB_DIR" "$GST_PLUGIN_DIR" "$GST_LIB_DIR/lib"; do
            [ -f "$search_dir/$basename" ] && echo "$search_dir/$basename" && return
            [ -f "$search_dir/$flat" ] && echo "$search_dir/$flat" && return
        done
        # Also try the exact flat name in the main lib dir
        [ -f "$GST_LIB_DIR/$flat" ] && echo "$GST_LIB_DIR/$flat" && return
        return 1
    }

    # Recursively copy all @rpath dependencies from the GStreamer framework
    copy_gst_deps() {
        for dep in $(otool -L "$1" 2>/dev/null | grep '@rpath/' | awk '{print $1}'); do
            local flat_name
            flat_name="$(basename "$dep")"
            if [ -f "$GSTREAMER_DST/$flat_name" ]; then
                continue  # already copied
            fi
            local resolved
            if resolved="$(resolve_rpath_dep "$dep")"; then
                cp "$resolved" "$GSTREAMER_DST/$flat_name"
                copy_gst_deps "$GSTREAMER_DST/$flat_name"
            fi
        done
    }

    # Start from libservo_ffi and all plugins
    copy_gst_deps "$NATIVE_LIB"
    for f in "$GSTREAMER_DST"/*.dylib; do
        [ -f "$f" ] && copy_gst_deps "$f"
    done

    # Rewrite all @rpath references in copied dylibs to use @loader_path
    # so they find each other in the flat lib/ directory
    rewrite_gst_rpaths() {
        for f in "$GSTREAMER_DST"/*.dylib; do
            [ -f "$f" ] || continue
            for dep in $(otool -L "$f" 2>/dev/null | grep '@rpath/' | awk '{print $1}'); do
                local flat_name
                flat_name="$(basename "$dep")"
                install_name_tool -change "$dep" "@loader_path/$flat_name" "$f" 2>/dev/null || true
            done
            # Also rewrite the install name itself if it uses @rpath
            local id
            id="$(otool -D "$f" 2>/dev/null | tail -1)"
            if [[ "$id" == @rpath/* ]]; then
                local flat_id
                flat_id="$(basename "$id")"
                install_name_tool -id "@loader_path/$flat_id" "$f" 2>/dev/null || true
            fi
        done
    }
    rewrite_gst_rpaths

    # Add rpath to the copied libservo_ffi so it can find GStreamer libs at runtime
    COPIED_LIB="$NATIVE_DIR/${LIB_PREFIX}servo_ffi.$LIB_EXT"
    install_name_tool -add_rpath @loader_path/lib "$COPIED_LIB" 2>/dev/null || true

    GSTCOUNT=$(ls -1 "$GSTREAMER_DST" 2>/dev/null | wc -l | tr -d ' ')
    echo "  GStreamer: $GSTCOUNT libraries → artifacts/lib/"
fi