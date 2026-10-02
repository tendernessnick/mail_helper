#!/usr/bin/env bash
# MailHelper .app 组装（MS9，docs/10 §9）：publish 产物 → MailHelper.app（Info.plist + icns）
# 用法: ./bundle.sh <publish目录> <输出.app路径> <版本号>
# 可选环境：CODESIGN_IDENTITY（有 Developer ID 时签名；缺省跳过签名——无证书路径不阻塞发布）
set -euo pipefail

PUBLISH="${1:?用法: bundle.sh <publish目录> <输出.app> <版本>}"
APP_PATH="${2:?…}"
VERSION="${3:?…}"
REPO="$(cd "$(dirname "$0")/../.." && pwd)"

APP="$APP_PATH/MailHelper.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# 1) 载荷
cp -R "$PUBLISH/" "$APP/Contents/MacOS/"
# 可执行入口名对齐 CFBundleExecutable（Avalonia 主程序）
EXE="$APP/Contents/MacOS/MailHelper.App.Avalonia"
test -f "$EXE" || { echo "缺少主程序 MailHelper.App.Avalonia"; exit 1; }
chmod +x "$EXE"

# 2) Info.plist（docs/10 §9 键集；net8 ⇒ LSMinimumSystemVersion 12.0）
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MailHelper</string>
  <key>CFBundleDisplayName</key><string>MailHelper 邮箱管家</string>
  <key>CFBundleIdentifier</key><string>com.mailhelper.app</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleExecutable</key><string>MailHelper.App.Avalonia</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>MailHelper</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# 3) icns
"$(dirname "$0")/make-icns.sh" "$REPO/src/MailHelper.App.Avalonia/Assets/icon-source.png" \
  "$APP/Contents/Resources/MailHelper.icns"

# 4) 签名（有证书时；无证书路径=跳过，Gatekeeper 首启指引见发布页）
if [ -n "${CODESIGN_IDENTITY:-}" ]; then
  codesign --deep --force --options runtime --timestamp \
    --sign "$CODESIGN_IDENTITY" "$APP"
  echo "已签名（$CODESIGN_IDENTITY）"
else
  echo "未签名（无 CODESIGN_IDENTITY；无证书路径）"
fi

# 5) 结构自检
plutil -lint "$APP/Contents/Info.plist"
test -d "$APP/Contents/MacOS" && test -s "$APP/Contents/Resources/MailHelper.icns"
echo "bundle OK: $APP"
