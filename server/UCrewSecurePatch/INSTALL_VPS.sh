#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

find_app_root() {
  local candidates=()

  if [[ -n "${APP_ROOT:-}" ]]; then
    candidates+=("$APP_ROOT")
  fi

  candidates+=(
    "/var/www/api.u-crew.net/ucrew_patch_v3"
    "/var/www/api.u-crew.net"
  )

  local candidate
  for candidate in "${candidates[@]}"; do
    if [[ -f "$candidate/includes/bootstrap.php" && -f "$candidate/api/auth_v2.php" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

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

if ! APP_ROOT_RESOLVED="$(find_app_root)"; then
  echo "U-CREW API uygulama kökü otomatik bulunamadı."
  echo "Kontrol edilen yollar:"
  echo "  /var/www/api.u-crew.net/ucrew_patch_v3"
  echo "  /var/www/api.u-crew.net"
  echo ""
  echo "Özel yol kullanımı:"
  echo "  APP_ROOT=/gercek/yol bash INSTALL_VPS.sh"
  exit 1
fi

APP_ROOT="$APP_ROOT_RESOLVED"
API_DIR="$APP_ROOT/api"
PATCH_ROOT="$APP_ROOT/secure_patches"
BACKUP_ROOT="$APP_ROOT/.ucrew_secure_backup_$(date +%Y%m%d_%H%M%S)"

install -d -o root -g root -m 0750 "$BACKUP_ROOT"

for current in \
  "$API_DIR/secure_patch_request.php" \
  "$API_DIR/secure_patch_download.php"; do
  if [[ -f "$current" ]]; then
    cp -a "$current" "$BACKUP_ROOT/"
  fi
done

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

Algılanan uygulama kökü:
  $APP_ROOT

API:
  $API_DIR/secure_patch_request.php
  $API_DIR/secure_patch_download.php

Bütün oyunların şifreli paket kökü:
  $PATCH_ROOT/<game_slug>/

Eski API dosyalarının yedeği:
  $BACKUP_ROOT

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
