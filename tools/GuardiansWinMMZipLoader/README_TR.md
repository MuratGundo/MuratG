# Guardians WinMM ZIP Loader

Bu proje, **Marvel's Guardians of the Galaxy: The Telltale Series**
için x64 `winmm.dll` proxy ve ZIP tabanlı gevşek kaynak yönlendirmesi üretir.

## Doğrulanan EXE

- Mimari: x64
- İçe aktarılan WinMM fonksiyonları:
  - `timeBeginPeriod`
  - `timeEndPeriod`
  - `timeGetTime`

Proxy yalnızca bu üç WinMM fonksiyonunu gerçek
`C:\Windows\System32\winmm.dll` dosyasına iletir.

## Çalışma biçimi

1. `UCREW_Guardians_TR.zip` oyun klasöründe aranır.
2. ZIP içeriği `%LOCALAPPDATA%\UCREW\Guardians\Cache` altına çıkarılır.
3. MinHook ile şu Windows API çağrıları yönlendirilir:
   - `CreateFileW`
   - `GetFileAttributesW`
   - `GetFileAttributesExW`
   - `FindFirstFileW`
4. Oyun ZIP'teki bir LANDb veya font dosyasını istediğinde önbellekteki
   Türkçe dosya açılır.
5. Eşleşme yoksa orijinal oyun dosyası kullanılır.

## Derleme

GitHub Actions:

`Actions > Build Guardians WinMM ZIP Loader > Run workflow`

Yerel:

`BUILD_X64.bat`

Visual Studio 2022 C++ ve CMake bileşenleri gerekir.

## Test

Önce yalnızca bir adet, oyunda gevşek olarak çalıştığı kesinleşmiş LANDb
dosyasıyla ZIP oluşturun. Oyun açılırsa `ucrew_winmm.log` dosyasını
kontrol edin.
