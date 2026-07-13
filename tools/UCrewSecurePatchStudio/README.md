# U-CREW Studio — Genel Güvenli Yama

Tek arayüzden bütün güvenli yama iş akışını yönetir:

1. VPS ve MySQL kurulumunu yapar.
2. Oyun profili oluşturur ve düzenler.
3. Yama klasörü veya ZIP dosyasından AES-256 şifreli `.ucp` paketi üretir.
4. SHA-256, metadata ve MySQL kayıt SQL dosyasını oluşturur.
5. Paketi SFTP ile VPS'e yükler.
6. Sunucu SHA-256 doğrulamasını yapar.
7. Yeni MySQL `users/games/licenses` sistemi üzerinden paketi kaydeder.
8. İşlem günlüklerini tek ekranda gösterir.

## Güvenlik

- SSH ve MySQL şifreleri kaydedilmez.
- Eski UUM/SQLite sistemi kullanılmaz.
- Sunucu dosyaları uygulama içine gömülüdür.
- Şifreli paket biçimi `zip-aes256-cbc-v1` olarak korunur.
- İstemci ve sunucu mevcut U-CREW Patch v3 mimarisiyle çalışır.

## İlk kullanım

1. `UCREW-Secure-Patch-Studio.exe` dosyasını çalıştır.
2. **Sunucu Kurulumu** bölümünde VPS ve MySQL bilgilerini gir.
3. **Oyun Profilleri** bölümünde oyun profilini kaydet.
4. **Yama Paketi** bölümünde profil, yama kaynağı, sürüm ve çıktı klasörünü seç.
5. **Sunucuya Yayınla** bölümünde oluşan metadata dosyasını seçip yayınla.

Guardians örnek profili paket içinde `guardians_profile.json` adıyla bulunur.
