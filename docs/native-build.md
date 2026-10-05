# Building the `spark_frost` native library

The **NSpark** package depends on `libspark_frost`, a Rust cdylib built from
the [buildonspark/spark](https://github.com/buildonspark/spark) repository.
C# bindings are auto-generated using
[`uniffi-bindgen-cs`](https://github.com/NordSecurity/uniffi-bindgen-cs).

Pre-built binaries for the supported RIDs ship inside the NSpark NuGet at
`runtimes/<rid>/native/`. SHA-256 hashes of every shipped binary are at
`runtimes/SHA256SUMS.txt` and on each
[GitHub release](https://github.com/p-i-g-g-y/nspark/releases). You
only need this document if you want to **rebuild** the native library
yourself or add support for a new RID.

## Supported runtime identifiers

This fork ships three RIDs, all rebuilt from Lightspark's source at a pinned
commit by `scripts/build-native.sh` (locally) and
`.github/workflows/native-build.yml` (CI):

| RID | Built on | Notes |
|---|---|---|
| `linux-x64`   | Amazon Linux 2023 container | glibc ≤ 2.34 (AWS Lambda floor), stripped |
| `linux-arm64` | Amazon Linux 2023 container | glibc ≤ 2.34, stripped |
| `osx-arm64`   | macOS, `MACOSX_DEPLOYMENT_TARGET=11.0` | stripped, ad-hoc signed |

`osx-x64`, `win-x64` and `win-arm64` are not shipped: their upstream binaries
were built outside CI from a different Spark revision, and loading them with
the current bindings is not verified.

Every build pins `SPARK_SHA` (the same commit the protos are vendored from),
uses `cargo build --locked`, a pinned `protoc`, and the Rust toolchain from
`signer/rust-toolchain.toml`, and checks:

- the 34 `uniffi_spark_frost_checksum_func_*` exports the bindings verify at load;
- on Linux, that no symbol needs a glibc newer than 2.34;
- that `uniffi-bindgen-cs` regenerates `SparkFrostBindings.cs` byte-for-byte.

The CI workflow also writes `SHA256SUMS.txt` and attaches build-provenance
attestations to each library. Verify a committed tree with:

```bash
( cd src/NSpark/runtimes && shasum -a 256 -c SHA256SUMS.txt )
```

### Rebuilding locally

```bash
SPARK_SRC=~/spark   # buildonspark/spark checked out at SPARK_SHA (see native-build.yml)
# Per-platform digests of the pinned amazonlinux:2023 index (Docker can't pull a
# second platform of an index digest it already holds).
AL2023_ARM64=amazonlinux@sha256:442a915d07801a5805b765f4f676f455a0007af70628aa34cd73a735db5740b3
AL2023_AMD64=amazonlinux@sha256:2851878b108218cccbe7af6ab7dfb87a5883a725623384e19e6256f4072027a3
docker run --rm --platform linux/arm64 -v "$SPARK_SRC:/spark:ro" -v "$PWD/scripts:/scripts:ro" \
  -v "$PWD/out:/out" "$AL2023_ARM64" bash /scripts/build-native.sh linux-arm64 /spark /out
docker run --rm --platform linux/amd64 -v "$SPARK_SRC:/spark:ro" -v "$PWD/scripts:/scripts:ro" \
  -v "$PWD/out:/out" "$AL2023_AMD64" bash /scripts/build-native.sh linux-x64 /spark /out
scripts/build-native.sh osx-arm64 "$SPARK_SRC" out
```

Copy
`out/runtimes/<rid>/native/*` into `src/NSpark/runtimes/` and regenerate
`SHA256SUMS.txt`. The sections below are upstream's original manual notes.

## Prerequisites

- **Rust toolchain** (1.88+): `curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh`
- **protoc** (protobuf compiler): `brew install protobuf` (macOS) or `apt install protobuf-compiler` (Linux)
- **uniffi-bindgen-cs** (v0.9.2, matching UniFFI 0.28.3):
  ```bash
  cargo install uniffi-bindgen-cs \
    --git https://github.com/NordSecurity/uniffi-bindgen-cs \
    --tag v0.9.2+v0.28.3
  ```

## Building the Native Library

### 1. Clone the Spark repository

```bash
git clone --depth 1 https://github.com/buildonspark/spark.git /tmp/spark-build
```

### 2. Ensure spark-frost-uniffi is in the workspace

The workspace at `signer/Cargo.toml` may have `spark-frost-uniffi` commented out. Ensure it's listed:

```toml
[workspace]
members = [
    "spark-frost",
    "spark-frost-signer",
    "spark-frost-uniffi",
]
```

### 3. Build for the current platform

```bash
cd /tmp/spark-build/signer
cargo build --release -p spark-frost-uniffi
```

Output: `target/release/libspark_frost.{dylib,so,dll}`

### 4. Build for other platforms (cross-compilation)

Install the target and use `--target`. macOS / Linux cross-compilation is
straightforward; Windows gets its own dedicated section below because the
recommended toolchain (`cargo-xwin`) differs.

```bash
# Linux x64 (from macOS, requires a cross-linker — easiest via `cross`)
rustup target add x86_64-unknown-linux-gnu

# macOS x64 (from arm64 Mac — works natively)
rustup target add x86_64-apple-darwin
cargo build --release -p spark-frost-uniffi --target x86_64-apple-darwin
```

For Linux cross-compilation from macOS, [`cross`](https://github.com/cross-rs/cross)
is easier than wrangling linkers by hand:

```bash
cargo install cross --git https://github.com/cross-rs/cross
cross build --release -p spark-frost-uniffi --target x86_64-unknown-linux-gnu
cross build --release -p spark-frost-uniffi --target aarch64-unknown-linux-gnu
```

### 5. Building the Windows DLL on macOS (`cargo-xwin`)

**Recommended path for shipping the Windows binaries from a macOS dev box.**
`cargo-xwin` cross-compiles to the **MSVC** target — the same ABI that a
native Windows build produces — by downloading the Windows SDK headers and
import libraries under Microsoft's redistribution license. No Docker, no
Windows VM.

#### One-time setup

```bash
brew install llvm
cargo install cargo-xwin
rustup target add x86_64-pc-windows-msvc aarch64-pc-windows-msvc
```

#### Build

```bash
cd /tmp/spark-build/signer

# Windows x64
cargo xwin build --release -p spark-frost-uniffi --target x86_64-pc-windows-msvc

# Windows arm64
cargo xwin build --release -p spark-frost-uniffi --target aarch64-pc-windows-msvc
```

First build downloads ~500 MB of Windows SDK pieces into
`~/.cache/cargo-xwin/`; subsequent builds are local. The output `.dll` is
indistinguishable from one produced on a Windows host with MSVC.

#### Output locations

```
target/x86_64-pc-windows-msvc/release/spark_frost.dll
target/aarch64-pc-windows-msvc/release/spark_frost.dll
```

#### Sanity check

After building, confirm the exported UniFFI symbols match what
`src/NSpark/Signer/SparkFrostBindings.cs` imports. On macOS:

```bash
# Requires llvm-readobj from brew install llvm.
$(brew --prefix llvm)/bin/llvm-readobj --coff-exports \
  target/x86_64-pc-windows-msvc/release/spark_frost.dll | head -40
```

Look for names like `uniffi_spark_frost_fn_*` matching the `DllImport`
declarations in `SparkFrostBindings.cs`. If they are missing, the C#
bindings will throw `EntryPointNotFoundException` at first call.

### 6. Copy to `runtimes/` directory

The destination paths assume you are in the NSpark repository root.

```bash
# Set this to wherever you cloned the Spark repo.
SPARK_BUILD=/tmp/spark-build/signer

# macOS arm64
mkdir -p src/NSpark/runtimes/osx-arm64/native
cp "$SPARK_BUILD/target/release/libspark_frost.dylib" \
   src/NSpark/runtimes/osx-arm64/native/

# macOS x64
mkdir -p src/NSpark/runtimes/osx-x64/native
cp "$SPARK_BUILD/target/x86_64-apple-darwin/release/libspark_frost.dylib" \
   src/NSpark/runtimes/osx-x64/native/

# Linux x64
mkdir -p src/NSpark/runtimes/linux-x64/native
cp "$SPARK_BUILD/target/x86_64-unknown-linux-gnu/release/libspark_frost.so" \
   src/NSpark/runtimes/linux-x64/native/

# Linux arm64
mkdir -p src/NSpark/runtimes/linux-arm64/native
cp "$SPARK_BUILD/target/aarch64-unknown-linux-gnu/release/libspark_frost.so" \
   src/NSpark/runtimes/linux-arm64/native/

# Windows x64 (note: filename is spark_frost.dll, no "lib" prefix)
mkdir -p src/NSpark/runtimes/win-x64/native
cp "$SPARK_BUILD/target/x86_64-pc-windows-msvc/release/spark_frost.dll" \
   src/NSpark/runtimes/win-x64/native/

# Windows arm64
mkdir -p src/NSpark/runtimes/win-arm64/native
cp "$SPARK_BUILD/target/aarch64-pc-windows-msvc/release/spark_frost.dll" \
   src/NSpark/runtimes/win-arm64/native/
```

A scripted version is at [`scripts/build-native-windows.sh`](../scripts/build-native-windows.sh).

## Regenerating C# Bindings

If the Spark repo updates its UDL interface (`spark_frost.udl`), regenerate the C# bindings:

```bash
# Build the cdylib first (needed for proc-macro metadata)
cd /tmp/spark-build/signer
cargo build --release -p spark-frost-uniffi

# Generate C# from UDL
uniffi-bindgen-cs \
  spark-frost-uniffi/src/spark_frost.udl \
  --lib-file target/release/libspark_frost.dylib \
  --out-dir /tmp/spark-frost-cs/

# Copy into project (replacing existing)
cp /tmp/spark-frost-cs/spark_frost.cs \
  src/NSpark/Signer/SparkFrostBindings.cs
```

The generated file uses `namespace uniffi.spark_frost` and `internal` access modifier. All types (`KeyPackage`, `NonceResult`, `SigningCommitment`, etc.) and the `SparkFrostMethods` static class are generated automatically.

## Version Pinning

| Component | Version | Why |
|-----------|---------|-----|
| Spark repo | `main` (shallow clone) | Source of spark-frost-uniffi crate |
| UniFFI (in Spark) | 0.28.3 | Defined in `spark-frost-uniffi/Cargo.toml` |
| uniffi-bindgen-cs | v0.9.2+v0.28.3 | Must match the UniFFI version exactly |
| Rust toolchain | 1.92.0 (per `rust-toolchain.toml`) | Pinned by the Spark repo |

If the Spark repo upgrades its UniFFI version, you must install the matching uniffi-bindgen-cs tag. Check the [compatibility table](https://github.com/NordSecurity/uniffi-bindgen-cs#compatibility).

## Runtime directory layout

```
src/NSpark/
  runtimes/
    osx-arm64/native/libspark_frost.dylib    # ~3.8 MB
    osx-x64/native/libspark_frost.dylib
    linux-x64/native/libspark_frost.so
    linux-arm64/native/libspark_frost.so
    win-x64/native/spark_frost.dll            # note: no "lib" prefix on Windows
    win-arm64/native/spark_frost.dll
    SHA256SUMS.txt                            # generated at release time
```

`src/NSpark/NSpark.csproj` packs each present binary into the NuGet at
the canonical `runtimes/<rid>/native/` path so .NET's native library
resolver picks the right binary per RID at consumer runtime. Missing
RIDs do not break the build — they break consumers on those platforms.

## Troubleshooting

**`protoc` not found during build**: Install protobuf (`brew install protobuf` / `apt install protobuf-compiler`).

**`spark-frost-uniffi` not in workspace**: Edit `signer/Cargo.toml` to add it to the `members` list.

**uniffi-bindgen-cs panics in `--library` mode**: Use UDL mode instead:
```bash
uniffi-bindgen-cs spark-frost-uniffi/src/spark_frost.udl \
  --lib-file target/release/libspark_frost.dylib \
  --out-dir ./output/
```

**Checksum mismatch at runtime**: The generated C# bindings checksum must match the native library. Regenerate bindings from the same build you're shipping.
