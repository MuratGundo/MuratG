#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="${APP_ROOT:-/var/www/api.u-crew.net}"
API_DIR="$APP_ROOT/api"
PATCH_ROOT="$APP_ROOT/secure_patches"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Bu kurulum root yetkisiyle çalıştırılmalıdır."
  echo "Kullanım: sudo bash INSTALL_VPS.sh"
  exit 1
fi

for required in \
  "$SCRIPT_DIR/api/secure_patch_request.php" \
  "$SCRIPT_DIR/api/secure_patch_download.php"; do
  if [[ ! -f "$required" ]]; then
    echo "Eksik dosya: $required"
    exit 1
  fi
done

if [[ ! -d "$APP_ROOT" ]]; then
  echo "API uygulama kökü bulunamadı: $APP_ROOT"
  exit 1
fi

if [[ ! -f "$APP_ROOT/includes/bootstrap.php" ]]; then
  echo "bootstrap.php bulunamadı: $APP_ROOT/includes/bootstrap.php"
  exit 1
fi

if [[ ! -f "$API_DIR/auth_v2.php" ]]; then
  echo "auth_v2.php bulunamadı: $API_DIR/auth_v2.php"
  exit 1
fi

install -d -o www-data -g www-data -m 0750 "$API_DIR"
install -d -o www-data -g www-data -m 0750 "$PATCH_ROOT"

install -o www-data -g www-data -m 0640 \
  "$SCRIPT_DIR/api/secure_patch_request.php" \
  "$API_DIR/secure_patch_request.php"

install -o www-data -g www-data -m 0640 \
  "$SCRIPT_DIR/api/secure_patch_download.php" \
  "$API_DIR/secure_patch_download.php"

php -l "$API_DIR/secure_patch_request.php"
php -l "$API_DIR/secure_patch_download.php"

if command -v nginx >/dev/null 2>&1; then
  nginx -t
  systemctl reload nginx
fi

cat <<EOF

U-CREW GENEL GÜVENLİ YAMA API'Sİ KURULDU

API:
  $API_DIR/secure_patch_request.php
  $API_DIR/secure_patch_download.php

Bütün oyunların şifreli paket kökü:
  $PATCH_ROOT/<game_slug>/

Sonraki işlemler:
1. database/INSTALL_SCHEMA.sql dosyasını yeni MySQL veritabanında çalıştır.
2. Her oyun için profiles klasöründeki örneğe benzer bir JSON profil oluştur.
3. Windows'ta tools/UCREW_SECURE_PATCH_PACK.bat ile .ucp paketi üret.
4. .ucp dosyasını $PATCH_ROOT/<game_slug>/ içine yükle.
5. Üretilen *_REGISTER.sql dosyasını veritabanında çalıştır.
6. Yetkisiz uç nokta testi:

   curl -sS -X POST https://api.u-crew.net/api/secure_patch_request.php \
     -d 'slug=guardians' -d 'hwid=test'

Beklenen sonuç JSON oturum/lisans hatasıdır. HTML veya 404 gelmemelidir.
EOF
