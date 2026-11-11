#!/bin/bash
# Adds gtCTRL's alternate app icons (one per Kia paint, Native/iOS/AppIcons, made by Tools/Icon/make_icon.py) to
# the Xcode project Godot exports, so the app can switch its icon to match the car's paint (CarPaints.IconFor).
# Godot regenerates the project on every export, so run this after each gtCTRL export:
#
#   bash Native/iOS/add-app-icons.sh <folder that holds the exported .xcodeproj>
#
# nCTRL doesn't use this.
set -euo pipefail
export_dir="$(cd "${1:?Usage: add-app-icons.sh <folder that holds the exported .xcodeproj>}" && pwd)"
icons="$(cd "$(dirname "$0")/AppIcons" && pwd)"
project=$(ls -d "$export_dir"/*.xcodeproj | head -1)
name=$(basename "$project" .xcodeproj)
catalog="$export_dir/$name/Images.xcassets"
[ -d "$catalog" ] || { echo "No asset catalog at $catalog" >&2; exit 1; }

# Every icon set is one 1024 image plus its Dark and Tinted looks. The icon is already dark, so Dark reuses it;
# Tinted (also used by the Clear home screen) is Tinted.png, grayscale art iOS colors with the user's tint.
# Without it iOS washes the full-color icon into a flat blob.
icon_set() { # <folder> <1024 png>
  rm -rf "$1"; mkdir -p "$1"
  cp "$2" "$1/icon-1024.png"
  cp "$icons/Tinted.png" "$1/icon-tinted.png"
  cat > "$1/Contents.json" <<'JSON'
{"images":[
 {"idiom":"universal","platform":"ios","size":"1024x1024","filename":"icon-1024.png"},
 {"appearances":[{"appearance":"luminosity","value":"dark"}],"idiom":"universal","platform":"ios","size":"1024x1024","filename":"icon-1024.png"},
 {"appearances":[{"appearance":"luminosity","value":"tinted"}],"idiom":"universal","platform":"ios","size":"1024x1024","filename":"icon-tinted.png"}
],"info":{"author":"xcode","version":1}}
JSON
}
main_png=$(mktemp -t appicon).png
cp "$catalog/AppIcon.appiconset/Icon-1024.png" "$main_png"
icon_set "$catalog/AppIcon.appiconset" "$main_png"
rm -f "$main_png"

count=0
for png in "$icons"/AppIcon-*.png; do
  icon_set "$catalog/$(basename "$png" .png).appiconset" "$png"
  count=$((count+1))
done

# Compile every icon set in the catalog, the alternates included, into the app.
pbx="$project/project.pbxproj"
if ! grep -q "ASSETCATALOG_COMPILER_INCLUDE_ALL_APPICON_ASSETS" "$pbx"; then
  sed -i '' 's/\(\t*\)ASSETCATALOG_COMPILER_APPICON_NAME = AppIcon;/&\
\1ASSETCATALOG_COMPILER_INCLUDE_ALL_APPICON_ASSETS = YES;/' "$pbx"
fi
# A launch screen named after the splash's contents: iOS caches launch-screen snapshots (even across restarts) and only
# rebuilds one when the launch storyboard's name changes, so a new splash shows from the very first frame.
plist="$export_dir/$name/$name-Info.plist"
stamp=$(shasum "$catalog/SplashImage.imageset/"*.png | shasum | cut -c1-8)
launch="Launch Screen $stamp"
if [ -f "$export_dir/$name/Launch Screen.storyboard" ]; then
  mv "$export_dir/$name/Launch Screen.storyboard" "$export_dir/$name/$launch.storyboard"
  sed -i '' "s/Launch Screen\.storyboard/$launch.storyboard/g" "$pbx"
  /usr/libexec/PlistBuddy -c "Set :UILaunchStoryboardName $launch" "$plist"
fi
echo "Launch screen: $launch"
echo "Added $count alternate app icons to $name ($(grep -c ASSETCATALOG_COMPILER_INCLUDE_ALL_APPICON_ASSETS "$pbx") build configurations)."
