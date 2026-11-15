#!/bin/bash
# MyZoom 맥 설치·업데이트 (터미널 한 줄)
#   curl -fsSL https://github.com/uts1002/myzoom/releases/latest/download/install-mac.sh | bash
#
# - 최신 MyZoom-mac.zip 을 받아 ~/Applications/MyZoom.app 에 둔다 (관리자 암호 필요 없음).
# - curl 로 받은 파일에는 '인터넷에서 받은 파일' 표시가 붙지 않아 서명 없이도 열 수 있다.
# - 로그인할 때 자동 실행되게 하고, 바로 연다. 같은 명령을 다시 실행하면 최신 버전으로 바뀐다.
# - MYZOOM_ZIP_URL 로 받을 주소를 바꿀 수 있다 (시험용).
set -euo pipefail

URL="${MYZOOM_ZIP_URL:-https://github.com/uts1002/myzoom/releases/latest/download/MyZoom-mac.zip}"
DEST="$HOME/Applications"
APP="$DEST/MyZoom.app"
PLIST="$HOME/Library/LaunchAgents/com.eompage.myzoom.plist"

echo
echo "  MyZoom 설치"
echo

major="$(sw_vers -productVersion | cut -d. -f1)"
if [ "$major" -lt 14 ]; then
    echo "  MyZoom 은 macOS 14 (Sonoma) 이상에서 돌아가요. 지금 버전: $(sw_vers -productVersion)"
    echo "  시스템 설정 > 일반 > 소프트웨어 업데이트에서 macOS 를 올린 뒤 다시 실행해 주세요."
    exit 1
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "  최신 버전을 내려받는 중..."
if ! curl -fL --progress-bar "$URL" -o "$TMP/MyZoom-mac.zip"; then
    echo "  내려받지 못했어요. 인터넷 연결을 확인하고 다시 실행해 주세요."
    exit 1
fi
ditto -x -k "$TMP/MyZoom-mac.zip" "$TMP"

if pgrep -x MyZoom >/dev/null; then
    echo "  켜져 있는 MyZoom 을 닫는 중..."
    pkill -x MyZoom || true
    sleep 1
fi

mkdir -p "$DEST"
rm -rf "$APP"
mv "$TMP/MyZoom.app" "$APP"
xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

# 로그인할 때 자동 실행 (앱 메뉴 '로그인할 때 자동 실행'과 같은 파일)
mkdir -p "$(dirname "$PLIST")"
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.eompage.myzoom</string>
  <key>ProgramArguments</key>
  <array><string>/usr/bin/open</string><string>-g</string><string>$APP</string></array>
  <key>RunAtLoad</key><true/>
</dict>
</plist>
EOF

open "$APP"

echo
echo "  설치 끝! 화면 오른쪽 위 메뉴 막대에 돋보기 아이콘이 생겼어요."
echo "  ⌃1 화면 확대 · ⌃2 그리기 · ⌃3 타이머 · ⌃4 라이브 줌 (control 키 + 숫자)"
echo "  확대·그리기는 처음 쓸 때 '화면 기록' 권한을 켜야 해요. 앱이 안내해 줘요."
echo
