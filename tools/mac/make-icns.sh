#!/usr/bin/env bash
# MailHelper icns 生成（MS9，docs/10 §9）：icon-source.png → 全尺寸 iconset → iconutil
# 用法: ./make-icns.sh <源PNG> <输出.icns>；仅 macOS（sips/iconutil 系统自带）
set -euo pipefail

SRC="${1:?用法: make-icns.sh <源PNG> <输出.icns>}"
OUT="${2:?用法: make-icns.sh <源PNG> <输出.icns>}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
ICONSET="$WORK/MailHelper.iconset"
mkdir -p "$ICONSET"

for spec in "16" "16@2x" "32" "32@2x" "128" "128@2x" "256" "256@2x" "512" "512@2x"; do
  base="${spec%@*}"
  scale="${spec#*@}"
  if [ "$scale" = "@2x" ]; then
    px=$((base * 2))
    name="icon_${base}x${base}@2x.png"
  else
    px="$base"
    name="icon_${base}x${base}.png"
  fi
  sips -z "$px" "$px" "$SRC" --out "$ICONSET/$name" >/dev/null
done

iconutil -c icns "$ICONSET" -o "$OUT"
test -s "$OUT" && echo "icns OK: $OUT"
