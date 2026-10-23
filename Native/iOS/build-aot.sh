#!/bin/bash
set -euo pipefail
# Invoked by Vault.targets for each NativeAOT publish architecture.
if [ "$#" -ne 2 ]; then echo "Usage: build-aot.sh <runtime identifier> <output directory>" >&2; exit 2; fi
case "$1" in
  ios-arm64) sdk=iphoneos; triple=arm64-apple-ios15.0 ;;
  iossimulator-arm64) sdk=iphonesimulator; triple=arm64-apple-ios15.0-simulator ;;
  iossimulator-x64) sdk=iphonesimulator; triple=x86_64-apple-ios15.0-simulator ;;
  *) echo "Unsupported iOS runtime identifier." >&2; exit 2 ;;
esac
script_dir="$(cd "$(dirname "$0")" && pwd)"
output_dir="$2"
mkdir -p "$output_dir"
sdk_root="$(xcrun --sdk "$sdk" --show-sdk-path)"
xcrun --sdk "$sdk" clang++ -target "$triple" -isysroot "$sdk_root" -fobjc-arc -fmodules -std=c++17 -O2 \
  -c "$script_dir/NGarageVault.mm" -o "$output_dir/NGarageVault.o"
xcrun libtool -static -o "$output_dir/NGarageVault.a" "$output_dir/NGarageVault.o"
echo "Built the nCTRL vault for $1."
