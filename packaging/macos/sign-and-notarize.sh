#!/bin/bash
# Signs, notarizes and zips Ringside.app on a Mac. Run from the folder that holds Ringside.app.
#
#   IDENTITY="Developer ID Application: Knight AI+AV LLC (9NB97BZDR3)" \
#   KEYCHAIN=/path/to/job.keychain-db \
#   NOTARY_KEY=/path/AuthKey_XXXX.p8 NOTARY_KEY_ID=XXXX NOTARY_ISSUER=xxxxxxxx-xxxx-... \
#   ./sign-and-notarize.sh 2.0.0
#
# Signs inside-out (every Mach-O first, then the bundle), never --deep. Writes
# Ringside-<version>-osx-arm64.zip next to the app and prints sha256 for it.
set -euo pipefail

VERSION="${1:?version, e.g. 2.0.0}"
APP="Ringside.app"
ENT="$(cd "$(dirname "$0")" && pwd)/Ringside.entitlements"
: "${IDENTITY:?set IDENTITY}"
KC_ARGS=()
[ -n "${KEYCHAIN:-}" ] && KC_ARGS=(--keychain "$KEYCHAIN")

# Readable by everyone, executables executable: a bundle with owner-only files breaks for users.
chmod -R u+rwX,go+rX "$APP"

# 1. Every Mach-O inside Contents/MacOS except the main executable.
while IFS= read -r -d '' f; do
  if file -b "$f" | grep -q 'Mach-O'; then
    codesign --force --timestamp --options runtime --entitlements "$ENT" "${KC_ARGS[@]}" --sign "$IDENTITY" "$f"
  fi
done < <(find "$APP/Contents/MacOS" -type f ! -path "$APP/Contents/MacOS/Ringside" -print0)

# 2. The main executable, then the bundle.
codesign --force --timestamp --options runtime --entitlements "$ENT" "${KC_ARGS[@]}" --sign "$IDENTITY" "$APP/Contents/MacOS/Ringside"
codesign --force --timestamp --options runtime --entitlements "$ENT" "${KC_ARGS[@]}" --sign "$IDENTITY" "$APP"
codesign --verify --strict --verbose=2 "$APP"

# 3. Notarize and staple.
ZIP="Ringside-$VERSION-osx-arm64.zip"
ditto -c -k --keepParent "$APP" "notarize-$ZIP"
xcrun notarytool submit "notarize-$ZIP" --key "${NOTARY_KEY:?}" --key-id "${NOTARY_KEY_ID:?}" --issuer "${NOTARY_ISSUER:?}" --wait
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose=4 "$APP"
rm -f "notarize-$ZIP"

# 4. The download.
ditto -c -k --keepParent "$APP" "$ZIP"
shasum -a 256 "$ZIP"
