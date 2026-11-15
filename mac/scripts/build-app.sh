#!/usr/bin/env bash
# MyZoom.app 만들기 (macOS 에서만). Apple Silicon + Intel 유니버설, ad-hoc 서명, zip 까지.
#   bash mac/scripts/build-app.sh 1.2.0      → mac/dist/MyZoom.app, mac/dist/MyZoom-mac.zip
# 버전을 안 주면 0.0.0 (개발판 — 자동 업데이트 안 함).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-0.0.0}"

swift build -c release --arch arm64 --arch x86_64
BIN=".build/apple/Products/Release/MyZoom"

APP="dist/MyZoom.app"
rm -rf dist
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/MyZoom"

# 아이콘: Windows 판 ico 를 그대로 icns 로
ICON_KEY=""
if sips -s format png ../Assets/myzoom.ico --out dist/icon.png >/dev/null 2>&1; then
    mkdir -p dist/MyZoom.iconset
    for s in 16 32 128 256 512; do
        sips -z "$s" "$s" dist/icon.png --out "dist/MyZoom.iconset/icon_${s}x${s}.png" >/dev/null
        sips -z $((s * 2)) $((s * 2)) dist/icon.png --out "dist/MyZoom.iconset/icon_${s}x${s}@2x.png" >/dev/null
    done
    if iconutil -c icns dist/MyZoom.iconset -o "$APP/Contents/Resources/MyZoom.icns"; then
        ICON_KEY="<key>CFBundleIconFile</key><string>MyZoom</string>"
    fi
fi

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>com.eompage.myzoom</string>
  <key>CFBundleName</key><string>MyZoom</string>
  <key>CFBundleDisplayName</key><string>MyZoom</string>
  <key>CFBundleExecutable</key><string>MyZoom</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>엄태식 (eompage.com)</string>
  ${ICON_KEY}
</dict>
</plist>
EOF

codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
lipo -archs "$APP/Contents/MacOS/MyZoom"

(cd dist && ditto -c -k --keepParent MyZoom.app MyZoom-mac.zip)
echo "built $APP ($VERSION)"
