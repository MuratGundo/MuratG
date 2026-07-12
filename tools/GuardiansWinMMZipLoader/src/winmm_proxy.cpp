#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <mmsystem.h>
#include <MinHook.h>

extern "C" {
#include <miniz.h>
}

#include <algorithm>
#include <cstdint>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace fs = std::filesystem;

namespace {
HMODULE g_self = nullptr;
HMODULE g_realWinmm = nullptr;
std::once_flag g_realWinmmOnce;

using TimeBeginPeriodFn = MMRESULT(WINAPI*)(UINT);
using TimeEndPeriodFn = MMRESULT(WINAPI*)(UINT);
using TimeGetTimeFn = DWORD(WINAPI*)();
TimeBeginPeriodFn g_timeBeginPeriod = nullptr;
TimeEndPeriodFn g_timeEndPeriod = nullptr;
TimeGetTimeFn g_timeGetTime = nullptr;

using CreateFileWFn = HANDLE(WINAPI*)(LPCWSTR, DWORD, DWORD,
    LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
CreateFileWFn g_originalCreateFileW = nullptr;

fs::path g_gameDir;
fs::path g_logPath;
std::unordered_map<std::wstring, fs::path> g_filesByName;
std::unordered_set<std::wstring> g_ambiguousNames;

std::wstring Lower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(),
        [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    return value;
}

std::wstring Utf8ToWide(const char* text) {
    if (!text || !*text) return {};
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        text, -1, nullptr, 0);
    UINT cp = CP_UTF8;
    DWORD flags = MB_ERR_INVALID_CHARS;
    if (n <= 0) {
        cp = CP_ACP;
        flags = 0;
        n = MultiByteToWideChar(cp, flags, text, -1, nullptr, 0);
    }
    if (n <= 0) return {};
    std::wstring out(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(cp, flags, text, -1, out.data(), n);
    if (!out.empty() && out.back() == L'\0') out.pop_back();
    return out;
}

fs::path ModuleDirectory(HMODULE module) {
    std::vector<wchar_t> buffer(32768, L'\0');
    DWORD n = GetModuleFileNameW(module, buffer.data(),
        static_cast<DWORD>(buffer.size()));
    if (!n || n >= buffer.size()) return {};
    return fs::path(std::wstring(buffer.data(), n)).parent_path();
}

void Log(const std::wstring& text) {
    if (g_logPath.empty()) return;
    std::wofstream stream(g_logPath, std::ios::app);
    if (!stream) return;
    SYSTEMTIME st{};
    GetLocalTime(&st);
    stream << L'[' << st.wHour << L':' << st.wMinute << L':' << st.wSecond
           << L"] " << text << L'\n';
}

void LoadRealWinmm() {
    std::call_once(g_realWinmmOnce, [] {
        std::vector<wchar_t> systemDir(32768, L'\0');
        UINT n = GetSystemDirectoryW(systemDir.data(),
            static_cast<UINT>(systemDir.size()));
        if (!n || n >= systemDir.size()) return;

        fs::path path = fs::path(systemDir.data()) / L"winmm.dll";
        g_realWinmm = LoadLibraryW(path.c_str());
        if (!g_realWinmm) return;

        g_timeBeginPeriod = reinterpret_cast<TimeBeginPeriodFn>(
            GetProcAddress(g_realWinmm, "timeBeginPeriod"));
        g_timeEndPeriod = reinterpret_cast<TimeEndPeriodFn>(
            GetProcAddress(g_realWinmm, "timeEndPeriod"));
        g_timeGetTime = reinterpret_cast<TimeGetTimeFn>(
            GetProcAddress(g_realWinmm, "timeGetTime"));
    });
}

bool ReadAll(const fs::path& path, std::vector<std::uint8_t>& data) {
    std::ifstream stream(path, std::ios::binary | std::ios::ate);
    if (!stream) return false;
    const auto size = stream.tellg();
    if (size <= 0) return false;
    data.resize(static_cast<size_t>(size));
    stream.seekg(0, std::ios::beg);
    return static_cast<bool>(stream.read(
        reinterpret_cast<char*>(data.data()), size));
}

fs::path CacheDirectory() {
    std::vector<wchar_t> value(32768, L'\0');
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", value.data(),
        static_cast<DWORD>(value.size()));
    if (n && n < value.size()) {
        return fs::path(value.data()) / L"UCREW" / L"Guardians" / L"Cache";
    }
    return g_gameDir / L".ucrew_cache";
}

std::wstring ReadIniString(const fs::path& ini, const wchar_t* key,
    const wchar_t* fallback) {
    std::vector<wchar_t> buffer(32768, L'\0');
    GetPrivateProfileStringW(L"Loader", key, fallback, buffer.data(),
        static_cast<DWORD>(buffer.size()), ini.c_str());
    return buffer.data();
}

bool ExtractZip(const fs::path& zipPath, const fs::path& cacheDir) {
    std::vector<std::uint8_t> bytes;
    if (!ReadAll(zipPath, bytes)) {
        Log(L"ZIP okunamadı: " + zipPath.wstring());
        return false;
    }

    std::error_code ec;
    fs::remove_all(cacheDir, ec);
    ec.clear();
    fs::create_directories(cacheDir, ec);
    if (ec) {
        Log(L"Önbellek oluşturulamadı: " + cacheDir.wstring());
        return false;
    }

    mz_zip_archive zip{};
    if (!mz_zip_reader_init_mem(&zip, bytes.data(), bytes.size(), 0)) {
        Log(L"ZIP bozuk veya desteklenmiyor.");
        return false;
    }

    size_t extracted = 0;
    const mz_uint count = mz_zip_reader_get_num_files(&zip);
    for (mz_uint i = 0; i < count; ++i) {
        mz_zip_archive_file_stat stat{};
        if (!mz_zip_reader_file_stat(&zip, i, &stat) || stat.m_is_directory)
            continue;

        const std::wstring entryName = Utf8ToWide(stat.m_filename);
        const fs::path filename = fs::path(entryName).filename();
        if (filename.empty() || filename == L"." || filename == L"..")
            continue;

        size_t outputSize = 0;
        void* outputData = mz_zip_reader_extract_to_heap(
            &zip, i, &outputSize, 0);
        if (!outputData) {
            Log(L"ZIP girdisi çıkarılamadı: " + entryName);
            continue;
        }

        const fs::path outputPath = cacheDir / filename;
        std::ofstream output(outputPath, std::ios::binary | std::ios::trunc);
        if (output) {
            output.write(static_cast<const char*>(outputData),
                static_cast<std::streamsize>(outputSize));
        }
        const bool ok = static_cast<bool>(output);
        output.close();
        mz_free(outputData);
        if (!ok) continue;

        const std::wstring key = Lower(filename.wstring());
        auto [it, inserted] = g_filesByName.emplace(key, outputPath);
        if (!inserted && it->second != outputPath) {
            g_filesByName.erase(key);
            g_ambiguousNames.insert(key);
            Log(L"Aynı adlı ZIP girdisi atlandı: " + filename.wstring());
            continue;
        }
        ++extracted;
    }

    mz_zip_reader_end(&zip);
    Log(L"ZIP hazırlandı. Dosya sayısı: " + std::to_wstring(extracted));
    return extracted > 0;
}

fs::path ReplacementFor(LPCWSTR requested) {
    if (!requested || !*requested) return {};
    const std::wstring key = Lower(fs::path(requested).filename().wstring());
    if (key.empty() || g_ambiguousNames.contains(key)) return {};
    const auto found = g_filesByName.find(key);
    return found == g_filesByName.end() ? fs::path{} : found->second;
}

HANDLE WINAPI HookCreateFileW(LPCWSTR fileName, DWORD desiredAccess,
    DWORD shareMode, LPSECURITY_ATTRIBUTES securityAttributes,
    DWORD creationDisposition, DWORD flagsAndAttributes, HANDLE templateFile) {
    if (!g_originalCreateFileW) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }

    const bool writing = (desiredAccess & GENERIC_WRITE) != 0 ||
        creationDisposition == CREATE_ALWAYS ||
        creationDisposition == CREATE_NEW ||
        creationDisposition == TRUNCATE_EXISTING;

    if (!writing) {
        const fs::path replacement = ReplacementFor(fileName);
        if (!replacement.empty()) {
            return g_originalCreateFileW(replacement.c_str(), desiredAccess,
                shareMode, securityAttributes, OPEN_EXISTING,
                flagsAndAttributes, templateFile);
        }
    }

    return g_originalCreateFileW(fileName, desiredAccess, shareMode,
        securityAttributes, creationDisposition, flagsAndAttributes,
        templateFile);
}

bool InstallHook() {
    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        Log(L"MinHook başlatılamadı: " + std::to_wstring(init));
        return false;
    }

    const MH_STATUS create = MH_CreateHookApi(L"kernel32.dll", "CreateFileW",
        reinterpret_cast<LPVOID>(&HookCreateFileW),
        reinterpret_cast<LPVOID*>(&g_originalCreateFileW));
    if (create != MH_OK) {
        Log(L"CreateFileW hook oluşturulamadı: " + std::to_wstring(create));
        return false;
    }

    const MH_STATUS enable = MH_EnableHook(MH_ALL_HOOKS);
    if (enable != MH_OK) {
        Log(L"Hook etkinleştirilemedi: " + std::to_wstring(enable));
        return false;
    }

    Log(L"CreateFileW hook etkin.");
    return true;
}

DWORD WINAPI Bootstrap(LPVOID) {
    LoadRealWinmm();
    g_gameDir = ModuleDirectory(nullptr);
    if (g_gameDir.empty()) g_gameDir = ModuleDirectory(g_self);
    g_logPath = g_gameDir / L"ucrew_winmm.log";

    {
        std::wofstream clear(g_logPath, std::ios::trunc);
        if (clear) clear << L"U-CREW Guardians WinMM ZIP Loader\n";
    }

    const fs::path ini = g_gameDir / L"ucrew_loader.ini";
    const bool enabled = GetPrivateProfileIntW(
        L"Loader", L"Enabled", 1, ini.c_str()) != 0;
    if (!enabled) {
        Log(L"Loader INI üzerinden kapalı.");
        return 0;
    }

    const std::wstring zipName = ReadIniString(
        ini, L"Zip", L"UCREW_Guardians_TR.zip");
    const fs::path zipPath = g_gameDir / zipName;
    if (!fs::exists(zipPath)) {
        Log(L"ZIP bulunamadı; oyun normal devam edecek.");
        return 0;
    }

    if (!ExtractZip(zipPath, CacheDirectory())) return 0;
    if (!InstallHook()) return 0;

    Log(L"U-CREW ZIP yönlendirmesi hazır.");
    return 0;
}
} // namespace

extern "C" __declspec(dllexport) MMRESULT WINAPI timeBeginPeriod(UINT period) {
    LoadRealWinmm();
    return g_timeBeginPeriod ? g_timeBeginPeriod(period) : TIMERR_NOCANDO;
}

extern "C" __declspec(dllexport) MMRESULT WINAPI timeEndPeriod(UINT period) {
    LoadRealWinmm();
    return g_timeEndPeriod ? g_timeEndPeriod(period) : TIMERR_NOCANDO;
}

extern "C" __declspec(dllexport) DWORD WINAPI timeGetTime() {
    LoadRealWinmm();
    return g_timeGetTime ? g_timeGetTime() : GetTickCount();
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = instance;
        DisableThreadLibraryCalls(instance);
        HANDLE thread = CreateThread(nullptr, 0, Bootstrap, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
