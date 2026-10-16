#!/bin/bash
# Tests sign-and-notarize.sh on a fixture shaped like Ringside.app, with an ad-hoc signature:
# no keychain, no network, no prompts, nothing outside a temp folder.
#
# The fixture has what broke the first real run: managed .dll and .json files next to the
# Mach-O files in Contents/MacOS, plus a folder in Contents/Resources. It checks that
#   1. the script signs every file there and codesign --verify --strict passes on the bundle;
#   2. the bundle still verifies after a ditto zip round trip (the signatures of non-Mach-O files
#      live in extended attributes, which ditto keeps);
#   3. a plain zip round trip, which drops extended attributes, does not verify; that is why the
#      script zips with ditto. (Informational: printed, not required.)
# Usage: bash test-signing.sh   (from anywhere; exits non-zero if 1 or 2 fails)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d /tmp/ringside-signtest.XXXXXX)"
trap 'rm -rf "$WORK"' EXIT
APP="$WORK/Ringside.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/claude-plugin/hooks"

cp /usr/bin/true "$APP/Contents/MacOS/Ringside"            # the main executable (Mach-O)
cp /usr/bin/false "$APP/Contents/MacOS/libSkiaSharp.dylib" # a native library stand-in (Mach-O)
printf 'MZ managed assembly stand-in\n' > "$APP/Contents/MacOS/Ringside.dll"
printf 'MZ managed assembly stand-in\n' > "$APP/Contents/MacOS/Avalonia.Base.dll"
printf '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0"}}\n' > "$APP/Contents/MacOS/Ringside.deps.json"
printf '{"runtimeOptions":{"tfm":"net10.0"}}\n' > "$APP/Contents/MacOS/Ringside.runtimeconfig.json"
printf '{"modules":["./register.js"]}\n' > "$APP/Contents/Resources/claude-plugin/hooks/hooks.json"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>Ringside</string>
  <key>CFBundleIdentifier</key><string>com.knightaiav.ringside.signtest</string>
  <key>CFBundleName</key><string>Ringside</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.0.0</string>
  <key>CFBundleVersion</key><string>0.0.0</string>
</dict>
</plist>
PLIST

cd "$WORK"
echo "== 1. sign with the real script (ad-hoc, no notarization)"
IDENTITY=- SKIP_NOTARIZE=1 bash "$HERE/sign-and-notarize.sh" 0.0.0-test
codesign --verify --strict --verbose=2 Ringside.app
xattr Ringside.app/Contents/MacOS/Ringside.deps.json | grep -q com.apple.cs.CodeDirectory
echo "PASS 1: bundle verifies strictly; Ringside.deps.json carries its signature in xattrs"

echo "== 2. ditto zip round trip"
ditto -c -k --keepParent Ringside.app ditto.zip
mkdir ditto-out && ditto -x -k ditto.zip ditto-out
codesign --verify --strict --verbose=2 ditto-out/Ringside.app
echo "PASS 2: verifies after ditto zip and unzip"

echo "== 3. plain zip round trip (informational)"
zip -qry plain.zip Ringside.app
mkdir plain-out && (cd plain-out && unzip -q ../plain.zip)
if codesign --verify --strict ditto-out/Ringside.app >/dev/null 2>&1 &&
   ! codesign --verify --strict plain-out/Ringside.app >/dev/null 2>&1; then
  echo "AS EXPECTED 3: a plain zip drops the xattrs and the bundle no longer verifies"
else
  echo "NOTE 3: the plain zip copy also verified; ditto is still the safe choice"
fi
echo "ALL REQUIRED CHECKS PASSED"
