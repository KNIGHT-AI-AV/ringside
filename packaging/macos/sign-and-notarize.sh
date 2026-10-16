#!/bin/bash
# Signs, notarizes and zips Ringside.app on a Mac. Run from the folder that holds Ringside.app.
#
#   IDENTITY=<Developer ID Application certificate SHA-1, or its full name> \
#   KEYCHAIN=/path/to/signing.keychain-db \
#   NOTARY_KEY=/path/AuthKey_XXXX.p8 NOTARY_KEY_ID=XXXX NOTARY_ISSUER=xxxxxxxx-xxxx-... \
#   bash sign-and-notarize.sh 2.0.0
#
# Signs inside-out (every file in Contents/MacOS, then the main executable, then the bundle),
# never --deep, and stops on the first failure. Writes Ringside-<version>-osx-arm64.zip next to
# the app with ditto, which keeps the extended attributes that hold the signatures of non-Mach-O
# files, then checks the zip by unpacking it and verifying the copy.
#
# Test mode: IDENTITY=- (ad-hoc) and SKIP_NOTARIZE=1 sign and verify only; no keychain, no
# network. See test-signing.sh.
set -euo pipefail

VERSION="${1:?version, e.g. 2.0.0}"
APP="Ringside.app"
ENT="$(cd "$(dirname "$0")" && pwd)/Ringside.entitlements"
: "${IDENTITY:?set IDENTITY}"
KC_ARGS=()
[ -n "${KEYCHAIN:-}" ] && KC_ARGS=(--keychain "$KEYCHAIN")
TS_ARGS=(--timestamp)
[ "$IDENTITY" = "-" ] && TS_ARGS=(--timestamp=none)   # an ad-hoc signature cannot be timestamped

sign() {
  # "${KC_ARGS[@]+...}" keeps an empty array safe under set -u in macOS's bash 3.2.
  codesign --force "${TS_ARGS[@]}" --options runtime --entitlements "$ENT" \
    ${KC_ARGS[@]+"${KC_ARGS[@]}"} --sign "$IDENTITY" "$1"
}

# Readable by everyone, executables executable: a bundle with owner-only files breaks for users.
chmod -R u+rwX,go+rX "$APP"

# 1. Every file inside Contents/MacOS except the main executable, Mach-O or not. codesign treats
#    everything there as nested code, so the managed .dll and .json files need signatures too
#    (stored in extended attributes). This is Avalonia's documented route.
count=0
while IFS= read -r -d '' f; do
  sign "$f"
  count=$((count + 1))
done < <(find "$APP/Contents/MacOS" -type f ! -path "$APP/Contents/MacOS/Ringside" -print0)
echo "signed $count files in Contents/MacOS"

# 2. The main executable, then the bundle.
sign "$APP/Contents/MacOS/Ringside"
sign "$APP"
codesign --verify --strict --verbose=2 "$APP"

if [ -n "${SKIP_NOTARIZE:-}" ]; then
  echo "signed and verified; notarization skipped (SKIP_NOTARIZE)"
  exit 0
fi

# 3. Notarize and staple. Anything but Accepted stops here with Apple's log.
ZIP="Ringside-$VERSION-osx-arm64.zip"
ditto -c -k --keepParent "$APP" "notarize-$ZIP"
result="$(xcrun notarytool submit "notarize-$ZIP" --key "${NOTARY_KEY:?}" --key-id "${NOTARY_KEY_ID:?}" \
  --issuer "${NOTARY_ISSUER:?}" --wait --output-format json)"
echo "$result"
status="$(/usr/bin/plutil -extract status raw -o - - <<<"$result")"
if [ "$status" != "Accepted" ]; then
  id="$(/usr/bin/plutil -extract id raw -o - - <<<"$result")"
  xcrun notarytool log "$id" --key "$NOTARY_KEY" --key-id "$NOTARY_KEY_ID" --issuer "$NOTARY_ISSUER" || true
  echo "notarization status: $status" >&2
  exit 1
fi
rm -f "notarize-$ZIP"
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose=4 "$APP"

# 4. The download, checked the way a user gets it: unpacked again, then verified.
ditto -c -k --keepParent "$APP" "$ZIP"
check="$(mktemp -d /tmp/ringside-zipcheck.XXXXXX)"
ditto -x -k "$ZIP" "$check"
codesign --verify --strict --verbose=2 "$check/$APP"
spctl --assess --type execute --verbose=4 "$check/$APP"
rm -rf "$check"
shasum -a 256 "$ZIP"
