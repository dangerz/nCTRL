#!/bin/bash
set -euo pipefail
script_dir="$(cd "$(dirname "$0")" && pwd)"
project_dir="$(cd "$script_dir/../.." && pwd)"
build_dir="$script_dir/build"
output_dir="$project_dir/ios/plugins/ngarage_vault"
mkdir -p "$build_dir/device" "$build_dir/simulator" "$output_dir"
for target in device simulator; do
  if [ "$target" = device ]; then sdk=iphoneos; triple=arm64-apple-ios15.0; else sdk=iphonesimulator; triple=arm64-apple-ios15.0-simulator; fi
  sdk_root="$(xcrun --sdk "$sdk" --show-sdk-path)"
  xcrun --sdk "$sdk" clang++ -target "$triple" -isysroot "$sdk_root" -fobjc-arc -fmodules -std=c++17 -O2 \
    -c "$script_dir/NGarageVault.mm" -o "$build_dir/$target/NGarageVault.o"
  xcrun libtool -static -o "$build_dir/$target/NGarageVault.a" "$build_dir/$target/NGarageVault.o"
done
# Rebuild only this generated framework, never source files.
if [ -d "$output_dir/NGarageVault.xcframework" ]; then rm -rf "$output_dir/NGarageVault.xcframework"; fi
xcodebuild -create-xcframework -library "$build_dir/device/NGarageVault.a" \
  -library "$build_dir/simulator/NGarageVault.a" -output "$output_dir/NGarageVault.xcframework"
cp "$script_dir/NGarageVault.gdip" "$output_dir/NGarageVault.gdip"
echo "Built NGarageVault. Enable it under iOS export > Plugins."
