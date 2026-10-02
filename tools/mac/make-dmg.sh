#!/usr/bin/env bash
# MailHelper DMG 制作（MS9，docs/10 §9）：staging（.app + /Applications 链接）→ UDZO
# 用法: ./make-dmg.sh <.app路径> <输出.dmg>
# 可选环境：APPLE_NOTARY（prof 文件路径）——公证与 staple 属检查点③，缺省跳过
set -euo pipefail

APP="${1:?用法: make-dmg.sh <.app路径> <输出.dmg>}"
OUT="${2:?用法: make-dmg.sh <.app路径> <输出.dmg>}"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

cp -R "$APP" "$STAGE/MailHelper.app"
ln -s /Applications "$STAGE/Applications"

rm -f "$OUT"
hdiutil create -volname "MailHelper" -srcfolder "$STAGE" -format UDZO -ov "$OUT" >/dev/null
test -s "$OUT" && echo "DMG OK: $OUT"
