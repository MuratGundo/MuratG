# U-CREW Genel Güvenli Yama Sistemi

Bu paket tek bir oyuna özel değildir. U-CREW panelindeki bütün oyunlar aynı API, veritabanı ve paket hazırlayıcı üzerinden çalışır.

Her oyun yalnızca bir profil tanımlar:

- `game_slug`
- `game_exe`
- `target_path`
- `install_mode`
- `allowed_extensions`
- `cleanup_on_exit`
- `launcher_theme`

İlk örnek profil `profiles/guardians.json` dosyasındadır.
