#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="${APP_ROOT:-/var/www/api.u-crew.net}"
API_DIR="$APP_ROOT/api"
PATCH_DIR="$APP_ROOT/secure_patches/guardians"
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
install -d -o www-data -g www-data -m 0750 "$PATCH_DIR"

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

GUARDIANS GÜVENLİ API DOSYALARI KURULDU

API:
  $API_DIR/secure_patch_request.php
  $API_DIR/secure_patch_download.php

Şifreli paket klasörü:
  $PATCH_DIR

Sonraki işlemler:
1. INSTALL_SCHEMA.sql dosyasını yeni MySQL veritabanında çalıştır.
2. Windows'ta tools/GUARDIANS_SECURE_PACK.bat ile .ucrew paketi üret.
3. .ucrew dosyasını $PATCH_DIR içine yükle.
4. Oluşan REGISTER_IN_DATABASE.sql dosyasını veritabanında çalıştır.
5. Yetkisiz uç nokta testini çalıştır:

   curl -sS -X POST https://api.u-crew.net/api/secure_patch_request.php \
     -d 'slug=guardians' -d 'hwid=test'

Beklenen sonuç: JSON biçiminde oturum/lisans hatası. HTML veya 404 gelmemelidir.
EOF
