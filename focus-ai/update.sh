#!/usr/bin/env bash
# Focus AI — tek komutla güncelle (WSL / Git Bash / macOS / Linux).
# Son kodu çeker ve konteynerleri yeniden derleyip başlatır.
#
# Kullanım:
#   cd <repo>/focus-ai
#   ./update.sh
#
# Not: .env dosyan (API anahtarları) git'e girmez, korunur.

set -euo pipefail
cd "$(dirname "$0")"

branch="$(git rev-parse --abbrev-ref HEAD)"
echo "→ '$branch' dalından son değişiklikler çekiliyor..."
git pull

echo "→ Konteynerler yeniden derleniyor (birkaç dakika sürebilir)..."
docker compose up -d --build

echo ""
echo "✓ Güncel. Telefondan aç: http://100.67.250.8:3000"
