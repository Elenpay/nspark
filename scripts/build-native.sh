#!/usr/bin/env bash
# Builds libspark_frost (buildonspark/spark signer/spark-frost-uniffi) for one RID, reproducibly,
# and checks it against the committed C# bindings. Used by .github/workflows/native-build.yml and
# runnable locally:
#
#   Linux RIDs build inside Amazon Linux 2023 (glibc 2.34, the AWS Lambda floor):
#     docker run --rm --platform linux/arm64 -v "$SPARK_SRC:/spark:ro" -v "$PWD:/nspark" \
#       "$AL2023_IMAGE" bash /nspark/scripts/build-native.sh linux-arm64 /spark /nspark/out
#   macOS arm64 builds natively:
#     scripts/build-native.sh osx-arm64 "$SPARK_SRC" out
#
# The Spark checkout must be at SPARK_SHA (see native-build.yml). The script never touches it:
# the cargo target dir lives under the output dir.
set -euo pipefail

RID="${1:?usage: build-native.sh <rid> <spark-src> <out-dir>}"
SRC="$(cd "${2:?spark source dir}" && pwd)"
OUT="${3:?output dir}"
mkdir -p "$OUT" && OUT="$(cd "$OUT" && pwd)"

PROTOC_VERSION=29.3
GLIBC_MAX=2.34
EXPECTED_CHECKSUMS=34

case "$RID" in
  linux-x64)   TARGET=x86_64-unknown-linux-gnu;  LIB=libspark_frost.so;    PROTOC_ARCH=linux-x86_64;  PROTOC_SHA=3e866620c5be27664f3d2fa2d656b5f3e09b5152b42f1bedbf427b333e90021a ;;
  linux-arm64) TARGET=aarch64-unknown-linux-gnu; LIB=libspark_frost.so;    PROTOC_ARCH=linux-aarch_64; PROTOC_SHA=6427349140e01f06e049e707a58709a4f221ae73ab9a0425bc4a00c8d0e1ab32 ;;
  osx-arm64)   TARGET=aarch64-apple-darwin;      LIB=libspark_frost.dylib; PROTOC_ARCH=osx-aarch_64;  PROTOC_SHA=2b8a3403cd097f95f3ba656e14b76c732b6b26d7f183330b11e36ef2bc028765 ;;
  *) echo "unsupported RID: $RID" >&2; exit 2 ;;
esac

TOOLS="$OUT/.tools/$RID"
mkdir -p "$TOOLS"

if [[ "$RID" == linux-* ]] && command -v dnf > /dev/null; then
  # Amazon Linux 2023 build container: compiler, binutils and the tools below.
  dnf install -y -q gcc make tar gzip unzip findutils binutils git > /dev/null
fi

# Pinned protoc (spark-frost's build.rs runs prost-build).
if [ ! -x "$TOOLS/protoc/bin/protoc" ]; then
  curl -fsSL -o "$TOOLS/protoc.zip" \
    "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-${PROTOC_ARCH}.zip"
  echo "$PROTOC_SHA  $TOOLS/protoc.zip" | (command -v sha256sum > /dev/null && sha256sum -c - || shasum -a 256 -c -)
  rm -rf "$TOOLS/protoc" && mkdir -p "$TOOLS/protoc" && unzip -q "$TOOLS/protoc.zip" -d "$TOOLS/protoc"
fi
export PROTOC="$TOOLS/protoc/bin/protoc"

# Rust toolchain pinned by spark/signer/rust-toolchain.toml.
if ! command -v rustup > /dev/null; then
  curl -fsSL https://sh.rustup.rs | sh -s -- -y --profile minimal --default-toolchain none > /dev/null
  # shellcheck disable=SC1091
  source "${CARGO_HOME:-$HOME/.cargo}/env"
fi

cd "$SRC/signer"
rustup toolchain install > /dev/null
rustup target add "$TARGET" > /dev/null

# Native host builds: override signer/.cargo/config.toml's cross linkers.
export CARGO_TARGET_X86_64_UNKNOWN_LINUX_GNU_LINKER=gcc
export CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER=gcc
export CARGO_TARGET_DIR="$OUT/.target/$RID"
export RUSTFLAGS="--remap-path-prefix=$SRC=/spark --remap-path-prefix=${CARGO_HOME:-$HOME/.cargo}=/cargo"
[[ "$RID" == osx-* ]] && export MACOSX_DEPLOYMENT_TARGET=11.0

cargo build --locked --release -p spark-frost-uniffi --target "$TARGET"

DEST="$OUT/runtimes/$RID/native"
mkdir -p "$DEST"
cp "$CARGO_TARGET_DIR/$TARGET/release/$LIB" "$DEST/$LIB"

if [[ "$RID" == linux-* ]]; then
  strip --strip-unneeded "$DEST/$LIB"
  glibc="$(objdump -T "$DEST/$LIB" | grep -oE 'GLIBC_[0-9.]+' | sed 's/GLIBC_//' | sort -Vu | tail -1)"
  if [ "$(printf '%s\n%s\n' "$glibc" "$GLIBC_MAX" | sort -V | tail -1)" != "$GLIBC_MAX" ]; then
    echo "$LIB needs GLIBC_$glibc, above the $GLIBC_MAX floor" >&2; exit 1
  fi
  echo "glibc floor: $glibc (max allowed $GLIBC_MAX)"
  checksums="$(nm -D --defined-only "$DEST/$LIB" | grep -c uniffi_spark_frost_checksum_func_)"
else
  strip -x "$DEST/$LIB"
  codesign -f -s - "$DEST/$LIB"
  checksums="$(nm -gU "$DEST/$LIB" | grep -c uniffi_spark_frost_checksum_func_)"
fi

if [ "$checksums" -ne "$EXPECTED_CHECKSUMS" ]; then
  echo "expected $EXPECTED_CHECKSUMS uniffi checksum functions, found $checksums" >&2; exit 1
fi
echo "uniffi checksum functions: $checksums"
(cd "$OUT" && (command -v sha256sum > /dev/null && sha256sum "runtimes/$RID/native/$LIB" || shasum -a 256 "runtimes/$RID/native/$LIB"))
