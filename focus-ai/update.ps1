# Focus AI — tek komutla güncelle.
# Son kodu çeker ve konteynerleri yeniden derleyip başlatır.
#
# Kullanım (PC başındayken ya da uzak masaüstü / SSH ile bağlanınca):
#   cd <repo>\focus-ai
#   .\update.ps1
#
# Not: .env dosyan (API anahtarları, Tailscale ayarları) git'e girmez, korunur.

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

$branch = (git rev-parse --abbrev-ref HEAD)
Write-Host "→ '$branch' dalindan son degisiklikler cekiliyor..." -ForegroundColor Cyan
git pull

Write-Host "→ Konteynerler yeniden derleniyor (birkac dakika surebilir)..." -ForegroundColor Cyan
docker compose up -d --build

Write-Host ""
Write-Host "✓ Guncel. Telefondan ac: http://100.67.250.8:3000" -ForegroundColor Green
