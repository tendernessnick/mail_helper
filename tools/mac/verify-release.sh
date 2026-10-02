#!/usr/bin/env bash
# MailHelper 发布核对（MS10）：对发布产物生成/校验 SHA256 清单
# 用法: ./verify-release.sh <目录>            → 生成该目录内产物的 SHA256SUMS.txt
#       ./verify-release.sh <目录> --check   → 校验已有清单
set -euo pipefail

DIR="${1:?用法: verify-release.sh <目录> [--check]}"
cd "$DIR"

if [ "${2:-}" = "--check" ]; then
  shasum -a 256 -c SHA256SUMS.txt
  echo "校验通过"
else
  : > SHA256SUMS.txt
  for f in MailHelper-*.dmg MailHelper-*.exe MailHelper-*.zip; do
    [ -f "$f" ] && shasum -a 256 "$f" >> SHA256SUMS.txt
  done
  cat SHA256SUMS.txt
  echo "清单已生成: $DIR/SHA256SUMS.txt"
fi
