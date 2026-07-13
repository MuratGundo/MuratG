# U-CREW Genel Güvenli Yama İstemcisi

Bu istemci tek bir oyuna özel değildir. `ucrew_game.json` dosyasındaki `GameSlug` değeriyle sunucudan oyuna ait çalışma profilini ve şifreli paketi alır.

## Akış

1. U-CREW Launcher hatırlanan oturumunu okur.
2. Kullanıcı lisansını ve oyun slug değerini API üzerinden doğrular.
3. Şifreli `.ucp` paketini indirir veya doğrulanmış önbellekten kullanır.
4. SHA-256 doğrulaması yapar.
5. Paketi geçici gizli alanda AES-256-CBC ile çözer.
6. Sunucudaki `runtime_profile` kurallarına göre oyuna kurar.
7. Oyunu başlatır.
8. Oyun kapanınca geçici yama dosyalarını kaldırır ve eski dosyaları geri yükler.

## Desteklenen kurulum modları

- `overlay_flat`
- `overlay_tree`
- `replace_files`
- `archive_replace`
- `custom` profiller için ayrı adaptör gerekir.

## Dosyalar

- `UCREW_SecurePatch.exe`
- `ucrew_game.json`
- İsteğe bağlı oyun logosu ve arka plan görseli

İstemci günlükleri `%LOCALAPPDATA%\U-CREW\SecurePatch\<game_slug>\client.log` konumundadır.
