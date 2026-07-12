#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>
#include <MinHook.h>

extern "C" {
#include <miniz.h>
}

#include <algorithm>
#include <cstdint>
#include <cwctype>
#include <cwchar>
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
fs::path g_gameDir;
fs::path g_logPath;
bool g_diagnostic = true;

std::unordered_map<std::wstring, fs::path> g_filesByName;
std::unordered_set<std::wstring> g_ambiguousNames;
std::unordered_set<std::wstring> g_seenRequests;
std::mutex g_logMutex;
std::mutex g_seenMutex;

using CreateFileWFn = HANDLE(WINAPI*)(LPCWSTR, DWORD, DWORD,
    LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
using CreateFileAFn = HANDLE(WINAPI*)(LPCSTR, DWORD, DWORD,
    LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
using CreateFile2Fn = HANDLE(WINAPI*)(LPCWSTR, DWORD, DWORD, DWORD,
    LPCREATEFILE2_EXTENDED_PARAMETERS);
using GetFileAttributesWFn = DWORD(WINAPI*)(LPCWSTR);
using GetFileAttributesAFn = DWORD(WINAPI*)(LPCSTR);
using GetFileAttributesExWFn = BOOL(WINAPI*)(LPCWSTR,
    GET_FILEEX_INFO_LEVELS, LPVOID);
using GetFileAttributesExAFn = BOOL(WINAPI*)(LPCSTR,
    GET_FILEEX_INFO_LEVELS, LPVOID);
using FindFirstFileWFn = HANDLE(WINAPI*)(LPCWSTR, LPWIN32_FIND_DATAW);
using FindFirstFileAFn = HANDLE(WINAPI*)(LPCSTR, LPWIN32_FIND_DATAA);
using FindFirstFileExWFn = HANDLE(WINAPI*)(LPCWSTR, FINDEX_INFO_LEVELS,
    LPVOID, FINDEX_SEARCH_OPS, LPVOID, DWORD);
using FindFirstFileExAFn = HANDLE(WINAPI*)(LPCSTR, FINDEX_INFO_LEVELS,
    LPVOID, FINDEX_SEARCH_OPS, LPVOID, DWORD);

CreateFileWFn g_originalCreateFileW = nullptr;
CreateFileAFn g_originalCreateFileA = nullptr;
CreateFile2Fn g_originalCreateFile2 = nullptr;
GetFileAttributesWFn g_originalGetFileAttributesW = nullptr;
GetFileAttributesAFn g_originalGetFileAttributesA = nullptr;
GetFileAttributesExWFn g_originalGetFileAttributesExW = nullptr;
GetFileAttributesExAFn g_originalGetFileAttributesExA = nullptr;
FindFirstFileWFn g_originalFindFirstFileW = nullptr;
FindFirstFileAFn g_originalFindFirstFileA = nullptr;
FindFirstFileExWFn g_originalFindFirstFileExW = nullptr;
FindFirstFileExAFn g_originalFindFirstFileExA = nullptr;

std::wstring Lower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(),
        [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    return value;
}

std::wstring Utf8ToWide(const char* text) {
    if (!text || !*text) return {};

    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        text, -1, nullptr, 0);
    UINT codePage = CP_UTF8;
    DWORD flags = MB_ERR_INVALID_CHARS;

    if (count <= 0) {
        codePage = CP_ACP;
        flags = 0;
        count = MultiByteToWideChar(codePage, flags, text, -1, nullptr, 0);
    }

    if (count <= 0) return {};

    std::wstring result(static_cast<std::size_t>(count), L'\0');
    MultiByteToWideChar(codePage, flags, text, -1, result.data(), count);
    if (!result.empty() && result.back() == L'\0') result.pop_back();
    return result;
}

std::wstring AnsiToWide(const char* text) {
    if (!text || !*text) return {};
    const int count = MultiByteToWideChar(CP_ACP, 0, text, -1, nullptr, 0);
    if (count <= 0) return {};
    std::wstring result(static_cast<std::size_t>(count), L'\0');
    MultiByteToWideChar(CP_ACP, 0, text, -1, result.data(), count);
    if (!result.empty() && result.back() == L'\0') result.pop_back();
    return result;
}

std::string WideToAnsi(const std::wstring& text) {
    if (text.empty()) return {};
    const int count = WideCharToMultiByte(CP_ACP, 0, text.c_str(), -1,
        nullptr, 0, nullptr, nullptr);
    if (count <= 0) return {};
    std::string result(static_cast<std::size_t>(count), '\0');
    WideCharToMultiByte(CP_ACP, 0, text.c_str(), -1, result.data(), count,
        nullptr, nullptr);
    if (!result.empty() && result.back() == '\0') result.pop_back();
    return result;
}

fs::path ModuleDirectory(HMODULE module) {
    std::vector<wchar_t> buffer(32768, L'\0');
    const DWORD count = GetModuleFileNameW(module, buffer.data(),
        static_cast<DWORD>(buffer.size()));
    if (!count || count >= buffer.size()) return {};
    return fs::path(std::wstring(buffer.data(), count)).parent_path();
}

void Log(const std::wstring& text) {
    if (g_logPath.empty()) return;
    std::lock_guard<std::mutex> guard(g_logMutex);
    std::wofstream stream(g_logPath, std::ios::app);
    if (!stream) return;

    SYSTEMTIME time{};
    GetLocalTime(&time);
    stream << L'[' << time.wHour << L':' << time.wMinute << L':'
           << time.wSecond << L"] " << text << L'\n';
}

bool IsPatchExtension(const std::wstring& requested) {
    const std::wstring extension = Lower(fs::path(requested).extension().wstring());
    return extension == L".landb" || extension == L".font" ||
        extension == L".fnt" || extension == L".dds" ||
        extension == L".d3dtx";
}

void LogRequest(const wchar_t* api, const std::wstring& requested,
    const fs::path& replacement) {
    if (requested.empty()) return;
    if (replacement.empty() && (!g_diagnostic || !IsPatchExtension(requested)))
        return;

    const std::wstring key = std::wstring(api) + L"|" + Lower(requested);
    bool first = false;
    {
        std::lock_guard<std::mutex> guard(g_seenMutex);
        first = g_seenRequests.insert(key).second;
    }
    if (!first) return;

    if (replacement.empty()) {
        Log(std::wstring(L"ISTEK AMA ZIPTE YOK [") + api + L"] " + requested);
    } else {
        Log(std::wstring(L"YONLENDIRME [") + api + L"] " + requested +
            L" => " + replacement.wstring());
    }
}

bool ReadAll(const fs::path& path, std::vector<std::uint8_t>& data) {
    std::ifstream stream(path, std::ios::binary | std::ios::ate);
    if (!stream) return false;
    const auto size = stream.tellg();
    if (size <= 0) return false;
    data.resize(static_cast<std::size_t>(size));
    stream.seekg(0, std::ios::beg);
    return static_cast<bool>(stream.read(
        reinterpret_cast<char*>(data.data()), size));
}

fs::path CacheDirectory() {
    std::vector<wchar_t> value(32768, L'\0');
    const DWORD count = GetEnvironmentVariableW(L"LOCALAPPDATA", value.data(),
        static_cast<DWORD>(value.size()));
    if (count && count < value.size()) {
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

    std::error_code error;
    fs::remove_all(cacheDir, error);
    error.clear();
    fs::create_directories(cacheDir, error);
    if (error) {
        Log(L"Önbellek oluşturulamadı: " + cacheDir.wstring());
        return false;
    }

    mz_zip_archive zip{};
    if (!mz_zip_reader_init_mem(&zip, bytes.data(), bytes.size(), 0)) {
        Log(L"ZIP bozuk veya desteklenmiyor.");
        return false;
    }

    std::size_t extracted = 0;
    const mz_uint count = mz_zip_reader_get_num_files(&zip);

    for (mz_uint index = 0; index < count; ++index) {
        mz_zip_archive_file_stat stat{};
        if (!mz_zip_reader_file_stat(&zip, index, &stat) || stat.m_is_directory)
            continue;

        const std::wstring entryName = Utf8ToWide(stat.m_filename);
        const fs::path filename = fs::path(entryName).filename();
        if (filename.empty() || filename == L"." || filename == L"..")
            continue;

        std::size_t outputSize = 0;
        void* outputData = mz_zip_reader_extract_to_heap(
            &zip, index, &outputSize, 0);
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
        auto [iterator, inserted] = g_filesByName.emplace(key, outputPath);
        if (!inserted && iterator->second != outputPath) {
            g_filesByName.erase(key);
            g_ambiguousNames.insert(key);
            Log(L"Aynı adlı ZIP girdisi atlandı: " + filename.wstring());
            continue;
        }

        if (g_diagnostic) {
            Log(L"ZIP DOSYASI: " + filename.wstring());
        }
        ++extracted;
    }

    mz_zip_reader_end(&zip);
    Log(L"ZIP hazırlandı. Dosya sayısı: " + std::to_wstring(extracted));
    return extracted > 0;
}

fs::path ReplacementForWide(const std::wstring& requested) {
    if (requested.empty()) return {};
    const std::wstring key = Lower(fs::path(requested).filename().wstring());
    if (key.empty() || g_ambiguousNames.contains(key)) return {};
    const auto found = g_filesByName.find(key);
    return found == g_filesByName.end() ? fs::path{} : found->second;
}

bool IsWriting(DWORD desiredAccess, DWORD creationDisposition) {
    return (desiredAccess & GENERIC_WRITE) != 0 ||
        creationDisposition == CREATE_ALWAYS ||
        creationDisposition == CREATE_NEW ||
        creationDisposition == TRUNCATE_EXISTING;
}

bool HasWildcard(const std::wstring& path) {
    return path.find(L'*') != std::wstring::npos ||
        path.find(L'?') != std::wstring::npos;
}

HANDLE WINAPI HookCreateFileW(LPCWSTR fileName, DWORD desiredAccess,
    DWORD shareMode, LPSECURITY_ATTRIBUTES securityAttributes,
    DWORD creationDisposition, DWORD flagsAndAttributes, HANDLE templateFile) {
    if (!g_originalCreateFileW) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }

    const std::wstring requested = fileName ? fileName : L"";
    fs::path replacement;
    if (!IsWriting(desiredAccess, creationDisposition)) {
        replacement = ReplacementForWide(requested);
    }
    LogRequest(L"CreateFileW", requested, replacement);

    if (!replacement.empty()) {
        return g_originalCreateFileW(replacement.c_str(), desiredAccess,
            shareMode, securityAttributes, OPEN_EXISTING,
            flagsAndAttributes, templateFile);
    }

    return g_originalCreateFileW(fileName, desiredAccess, shareMode,
        securityAttributes, creationDisposition, flagsAndAttributes,
        templateFile);
}

HANDLE WINAPI HookCreateFileA(LPCSTR fileName, DWORD desiredAccess,
    DWORD shareMode, LPSECURITY_ATTRIBUTES securityAttributes,
    DWORD creationDisposition, DWORD flagsAndAttributes, HANDLE templateFile) {
    if (!g_originalCreateFileA) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }

    const std::wstring requested = AnsiToWide(fileName);
    fs::path replacement;
    if (!IsWriting(desiredAccess, creationDisposition)) {
        replacement = ReplacementForWide(requested);
    }
    LogRequest(L"CreateFileA", requested, replacement);

    if (!replacement.empty()) {
        const std::string ansiPath = WideToAnsi(replacement.wstring());
        if (!ansiPath.empty()) {
            return g_originalCreateFileA(ansiPath.c_str(), desiredAccess,
                shareMode, securityAttributes, OPEN_EXISTING,
                flagsAndAttributes, templateFile);
        }
    }

    return g_originalCreateFileA(fileName, desiredAccess, shareMode,
        securityAttributes, creationDisposition, flagsAndAttributes,
        templateFile);
}

HANDLE WINAPI HookCreateFile2(LPCWSTR fileName, DWORD desiredAccess,
    DWORD shareMode, DWORD creationDisposition,
    LPCREATEFILE2_EXTENDED_PARAMETERS parameters) {
    if (!g_originalCreateFile2) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }

    const std::wstring requested = fileName ? fileName : L"";
    fs::path replacement;
    if (!IsWriting(desiredAccess, creationDisposition)) {
        replacement = ReplacementForWide(requested);
    }
    LogRequest(L"CreateFile2", requested, replacement);

    return g_originalCreateFile2(
        replacement.empty() ? fileName : replacement.c_str(), desiredAccess,
        shareMode, replacement.empty() ? creationDisposition : OPEN_EXISTING,
        parameters);
}

DWORD WINAPI HookGetFileAttributesW(LPCWSTR fileName) {
    if (!g_originalGetFileAttributesW) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_FILE_ATTRIBUTES;
    }
    const std::wstring requested = fileName ? fileName : L"";
    const fs::path replacement = ReplacementForWide(requested);
    LogRequest(L"GetFileAttributesW", requested, replacement);
    return g_originalGetFileAttributesW(
        replacement.empty() ? fileName : replacement.c_str());
}

DWORD WINAPI HookGetFileAttributesA(LPCSTR fileName) {
    if (!g_originalGetFileAttributesA) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_FILE_ATTRIBUTES;
    }
    const std::wstring requested = AnsiToWide(fileName);
    const fs::path replacement = ReplacementForWide(requested);
    LogRequest(L"GetFileAttributesA", requested, replacement);
    if (!replacement.empty()) {
        const std::string ansiPath = WideToAnsi(replacement.wstring());
        if (!ansiPath.empty()) return g_originalGetFileAttributesA(ansiPath.c_str());
    }
    return g_originalGetFileAttributesA(fileName);
}

BOOL WINAPI HookGetFileAttributesExW(LPCWSTR fileName,
    GET_FILEEX_INFO_LEVELS level, LPVOID information) {
    if (!g_originalGetFileAttributesExW) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }
    const std::wstring requested = fileName ? fileName : L"";
    const fs::path replacement = ReplacementForWide(requested);
    LogRequest(L"GetFileAttributesExW", requested, replacement);
    return g_originalGetFileAttributesExW(
        replacement.empty() ? fileName : replacement.c_str(), level,
        information);
}

BOOL WINAPI HookGetFileAttributesExA(LPCSTR fileName,
    GET_FILEEX_INFO_LEVELS level, LPVOID information) {
    if (!g_originalGetFileAttributesExA) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }
    const std::wstring requested = AnsiToWide(fileName);
    const fs::path replacement = ReplacementForWide(requested);
    LogRequest(L"GetFileAttributesExA", requested, replacement);
    if (!replacement.empty()) {
        const std::string ansiPath = WideToAnsi(replacement.wstring());
        if (!ansiPath.empty()) {
            return g_originalGetFileAttributesExA(ansiPath.c_str(), level,
                information);
        }
    }
    return g_originalGetFileAttributesExA(fileName, level, information);
}

HANDLE WINAPI HookFindFirstFileW(LPCWSTR fileName,
    LPWIN32_FIND_DATAW findData) {
    if (!g_originalFindFirstFileW) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }
    const std::wstring requested = fileName ? fileName : L"";
    const fs::path replacement = HasWildcard(requested)
        ? fs::path{} : ReplacementForWide(requested);
    LogRequest(L"FindFirstFileW", requested, replacement);
    return g_originalFindFirstFileW(
        replacement.empty() ? fileName : replacement.c_str(), findData);
}

HANDLE WINAPI HookFindFirstFileA(LPCSTR fileName,
    LPWIN32_FIND_DATAA findData) {
    if (!g_originalFindFirstFileA) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }
    const std::wstring requested = AnsiToWide(fileName);
    const fs::path replacement = HasWildcard(requested)
        ? fs::path{} : ReplacementForWide(requested);
    LogRequest(L"FindFirstFileA", requested, replacement);
    if (!replacement.empty()) {
        const std::string ansiPath = WideToAnsi(replacement.wstring());
        if (!ansiPath.empty()) return g_originalFindFirstFileA(ansiPath.c_str(), findData);
    }
    return g_originalFindFirstFileA(fileName, findData);
}

HANDLE WINAPI HookFindFirstFileExW(LPCWSTR fileName,
    FINDEX_INFO_LEVELS infoLevel, LPVOID findData,
    FINDEX_SEARCH_OPS searchOp, LPVOID searchFilter, DWORD flags) {
    if (!g_originalFindFirstFileExW) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }
    const std::wstring requested = fileName ? fileName : L"";
    const fs::path replacement = HasWildcard(requested)
        ? fs::path{} : ReplacementForWide(requested);
    LogRequest(L"FindFirstFileExW", requested, replacement);
    return g_originalFindFirstFileExW(
        replacement.empty() ? fileName : replacement.c_str(), infoLevel,
        findData, searchOp, searchFilter, flags);
}

HANDLE WINAPI HookFindFirstFileExA(LPCSTR fileName,
    FINDEX_INFO_LEVELS infoLevel, LPVOID findData,
    FINDEX_SEARCH_OPS searchOp, LPVOID searchFilter, DWORD flags) {
    if (!g_originalFindFirstFileExA) {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return INVALID_HANDLE_VALUE;
    }
    const std::wstring requested = AnsiToWide(fileName);
    const fs::path replacement = HasWildcard(requested)
        ? fs::path{} : ReplacementForWide(requested);
    LogRequest(L"FindFirstFileExA", requested, replacement);
    if (!replacement.empty()) {
        const std::string ansiPath = WideToAnsi(replacement.wstring());
        if (!ansiPath.empty()) {
            return g_originalFindFirstFileExA(ansiPath.c_str(), infoLevel,
                findData, searchOp, searchFilter, flags);
        }
    }
    return g_originalFindFirstFileExA(fileName, infoLevel, findData,
        searchOp, searchFilter, flags);
}

bool CreateHook(const char* name, LPVOID detour, LPVOID* original,
    bool required = false) {
    const MH_STATUS status = MH_CreateHookApi(L"kernel32.dll", name,
        detour, original);
    if (status == MH_OK) {
        Log(L"Hook oluşturuldu: " + Utf8ToWide(name));
        return true;
    }

    Log(L"Hook oluşturulamadı: " + Utf8ToWide(name) + L" | Kod: " +
        std::to_wstring(status));
    return !required;
}

bool InstallHooks() {
    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        Log(L"MinHook başlatılamadı: " + std::to_wstring(init));
        return false;
    }

    bool ok = true;
    ok &= CreateHook("CreateFileW", reinterpret_cast<LPVOID>(&HookCreateFileW),
        reinterpret_cast<LPVOID*>(&g_originalCreateFileW), true);
    ok &= CreateHook("CreateFileA", reinterpret_cast<LPVOID>(&HookCreateFileA),
        reinterpret_cast<LPVOID*>(&g_originalCreateFileA), true);
    CreateHook("CreateFile2", reinterpret_cast<LPVOID>(&HookCreateFile2),
        reinterpret_cast<LPVOID*>(&g_originalCreateFile2));
    CreateHook("GetFileAttributesW",
        reinterpret_cast<LPVOID>(&HookGetFileAttributesW),
        reinterpret_cast<LPVOID*>(&g_originalGetFileAttributesW));
    CreateHook("GetFileAttributesA",
        reinterpret_cast<LPVOID>(&HookGetFileAttributesA),
        reinterpret_cast<LPVOID*>(&g_originalGetFileAttributesA));
    CreateHook("GetFileAttributesExW",
        reinterpret_cast<LPVOID>(&HookGetFileAttributesExW),
        reinterpret_cast<LPVOID*>(&g_originalGetFileAttributesExW));
    CreateHook("GetFileAttributesExA",
        reinterpret_cast<LPVOID>(&HookGetFileAttributesExA),
        reinterpret_cast<LPVOID*>(&g_originalGetFileAttributesExA));
    CreateHook("FindFirstFileW", reinterpret_cast<LPVOID>(&HookFindFirstFileW),
        reinterpret_cast<LPVOID*>(&g_originalFindFirstFileW));
    CreateHook("FindFirstFileA", reinterpret_cast<LPVOID>(&HookFindFirstFileA),
        reinterpret_cast<LPVOID*>(&g_originalFindFirstFileA));
    CreateHook("FindFirstFileExW",
        reinterpret_cast<LPVOID>(&HookFindFirstFileExW),
        reinterpret_cast<LPVOID*>(&g_originalFindFirstFileExW));
    CreateHook("FindFirstFileExA",
        reinterpret_cast<LPVOID>(&HookFindFirstFileExA),
        reinterpret_cast<LPVOID*>(&g_originalFindFirstFileExA));

    if (!ok) {
        MH_Uninitialize();
        return false;
    }

    const MH_STATUS enable = MH_EnableHook(MH_ALL_HOOKS);
    if (enable != MH_OK) {
        Log(L"Hooklar etkinleştirilemedi: " + std::to_wstring(enable));
        MH_Uninitialize();
        return false;
    }

    Log(L"A/W File API hookları etkin.");
    return true;
}

DWORD WINAPI Bootstrap(LPVOID) {
    g_gameDir = ModuleDirectory(nullptr);
    if (g_gameDir.empty()) g_gameDir = ModuleDirectory(g_self);
    g_logPath = g_gameDir / L"ucrew_winmm.log";

    {
        std::wofstream clear(g_logPath, std::ios::trunc);
        if (clear) clear << L"U-CREW Guardians ZIP Loader v0.3\n";
    }

    const fs::path ini = g_gameDir / L"ucrew_loader.ini";
    const bool enabled = GetPrivateProfileIntW(
        L"Loader", L"Enabled", 1, ini.c_str()) != 0;
    g_diagnostic = GetPrivateProfileIntW(
        L"Loader", L"Diagnostic", 1, ini.c_str()) != 0;

    Log(L"Oyun klasörü: " + g_gameDir.wstring());
    Log(std::wstring(L"Tanılama: ") + (g_diagnostic ? L"Açık" : L"Kapalı"));

    if (!enabled) {
        Log(L"Loader INI üzerinden kapalı.");
        return 0;
    }

    const std::wstring zipName = ReadIniString(
        ini, L"Zip", L"UCREW_Guardians_TR.zip");
    const fs::path zipPath = g_gameDir / zipName;
    Log(L"ZIP: " + zipPath.wstring());

    if (!fs::exists(zipPath)) {
        Log(L"ZIP bulunamadı; oyun normal devam edecek.");
        return 0;
    }

    if (!ExtractZip(zipPath, CacheDirectory())) return 0;
    if (!InstallHooks()) return 0;

    Log(L"U-CREW ZIP yönlendirmesi hazır.");
    return 0;
}
} // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = instance;
        DisableThreadLibraryCalls(instance);
        HANDLE thread = CreateThread(nullptr, 0, Bootstrap, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
