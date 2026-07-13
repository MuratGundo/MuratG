#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>

#include <string>
#include <vector>

namespace
{
    HMODULE g_realVersion = nullptr;
    INIT_ONCE g_versionOnce = INIT_ONCE_STATIC_INIT;

    constexpr wchar_t ChildMarkerName[] = L"UCREW_BOOTSTRAP_CHILD";
    constexpr wchar_t HelperFileName[] = L"UCREW_Guardians_TR.exe";

    BOOL CALLBACK LoadRealVersion(PINIT_ONCE, PVOID, PVOID*)
    {
        std::vector<wchar_t> systemDirectory(32768, L'\0');

        const UINT length = GetSystemDirectoryW(
            systemDirectory.data(),
            static_cast<UINT>(systemDirectory.size()));

        if (length == 0 || length >= systemDirectory.size())
        {
            return TRUE;
        }

        std::wstring realPath(systemDirectory.data(), length);
        realPath += L"\\version.dll";

        g_realVersion = LoadLibraryW(realPath.c_str());
        return TRUE;
    }

    HMODULE EnsureRealVersion()
    {
        InitOnceExecuteOnce(
            &g_versionOnce,
            LoadRealVersion,
            nullptr,
            nullptr);

        return g_realVersion;
    }

    template <typename T>
    T Resolve(const char* functionName)
    {
        HMODULE module = EnsureRealVersion();

        if (module == nullptr)
        {
            return nullptr;
        }

        return reinterpret_cast<T>(
            GetProcAddress(module, functionName));
    }

    std::wstring GetModulePath(HMODULE module)
    {
        std::vector<wchar_t> buffer(32768, L'\0');

        const DWORD length = GetModuleFileNameW(
            module,
            buffer.data(),
            static_cast<DWORD>(buffer.size()));

        if (length == 0 || length >= buffer.size())
        {
            return {};
        }

        return std::wstring(buffer.data(), length);
    }

    std::wstring ParentDirectory(const std::wstring& path)
    {
        const std::size_t slash = path.find_last_of(L"\\/");

        if (slash == std::wstring::npos)
        {
            return {};
        }

        return path.substr(0, slash);
    }

    void SetHiddenSystem(const std::wstring& path)
    {
        const DWORD attributes = GetFileAttributesW(path.c_str());

        if (attributes == INVALID_FILE_ATTRIBUTES)
        {
            return;
        }

        SetFileAttributesW(
            path.c_str(),
            attributes | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM);
    }

    bool IsBootstrapChild()
    {
        wchar_t value[8]{};

        const DWORD length = GetEnvironmentVariableW(
            ChildMarkerName,
            value,
            static_cast<DWORD>(std::size(value)));

        return length > 0 && value[0] == L'1';
    }

    void LaunchPatchHelperAndWait(HMODULE selfModule)
    {
        if (IsBootstrapChild())
        {
            return;
        }

        const std::wstring selfPath = GetModulePath(selfModule);
        const std::wstring gameDirectory = ParentDirectory(selfPath);

        if (selfPath.empty() || gameDirectory.empty())
        {
            return;
        }

        SetHiddenSystem(selfPath);

        const std::wstring helperPath =
            gameDirectory + L"\\" + HelperFileName;

        if (GetFileAttributesW(helperPath.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            return;
        }

        SetHiddenSystem(helperPath);

        const DWORD processId = GetCurrentProcessId();
        const std::wstring eventName =
            L"Local\\UCREW_Guardians_Ready_" + std::to_wstring(processId);

        HANDLE readyEvent = CreateEventW(
            nullptr,
            TRUE,
            FALSE,
            eventName.c_str());

        if (readyEvent == nullptr)
        {
            return;
        }

        std::wstring commandLine =
            L"\"" + helperPath + L"\"" +
            L" --ucrew-parent-pid=" + std::to_wstring(processId) +
            L" \"--ucrew-ready-event=" + eventName + L"\"";

        std::vector<wchar_t> mutableCommandLine(
            commandLine.begin(),
            commandLine.end());
        mutableCommandLine.push_back(L'\0');

        SetEnvironmentVariableW(ChildMarkerName, L"1");

        STARTUPINFOW startupInfo{};
        startupInfo.cb = sizeof(startupInfo);

        PROCESS_INFORMATION processInfo{};

        const BOOL created = CreateProcessW(
            helperPath.c_str(),
            mutableCommandLine.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_UNICODE_ENVIRONMENT,
            nullptr,
            gameDirectory.c_str(),
            &startupInfo,
            &processInfo);

        SetEnvironmentVariableW(ChildMarkerName, nullptr);

        if (!created)
        {
            CloseHandle(readyEvent);
            return;
        }

        HANDLE waitHandles[2] =
        {
            readyEvent,
            processInfo.hProcess
        };

        WaitForMultipleObjects(
            2,
            waitHandles,
            FALSE,
            10UL * 60UL * 1000UL);

        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
        CloseHandle(readyEvent);
    }
}

extern "C" DWORD WINAPI UCrew_GetFileVersionInfoSizeA(
    LPCSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(LPCSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeA");
    return function ? function(filename, handle) : 0;
}

extern "C" DWORD WINAPI UCrew_GetFileVersionInfoSizeW(
    LPCWSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(LPCWSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeW");
    return function ? function(filename, handle) : 0;
}

extern "C" DWORD WINAPI UCrew_GetFileVersionInfoSizeExA(
    DWORD flags,
    LPCSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(DWORD, LPCSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeExA");
    return function ? function(flags, filename, handle) : 0;
}

extern "C" DWORD WINAPI UCrew_GetFileVersionInfoSizeExW(
    DWORD flags,
    LPCWSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(DWORD, LPCWSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeExW");
    return function ? function(flags, filename, handle) : 0;
}

extern "C" BOOL WINAPI UCrew_GetFileVersionInfoA(
    LPCSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(LPCSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoA");
    return function ? function(filename, handle, length, data) : FALSE;
}

extern "C" BOOL WINAPI UCrew_GetFileVersionInfoW(
    LPCWSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(LPCWSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoW");
    return function ? function(filename, handle, length, data) : FALSE;
}

extern "C" BOOL WINAPI UCrew_GetFileVersionInfoExA(
    DWORD flags,
    LPCSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(DWORD, LPCSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoExA");
    return function ? function(flags, filename, handle, length, data) : FALSE;
}

extern "C" BOOL WINAPI UCrew_GetFileVersionInfoExW(
    DWORD flags,
    LPCWSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(DWORD, LPCWSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoExW");
    return function ? function(flags, filename, handle, length, data) : FALSE;
}

extern "C" BOOL WINAPI UCrew_VerQueryValueA(
    LPCVOID block,
    LPCSTR subBlock,
    LPVOID* buffer,
    PUINT length)
{
    using Fn = BOOL(WINAPI*)(LPCVOID, LPCSTR, LPVOID*, PUINT);
    static Fn function = Resolve<Fn>("VerQueryValueA");
    return function ? function(block, subBlock, buffer, length) : FALSE;
}

extern "C" BOOL WINAPI UCrew_VerQueryValueW(
    LPCVOID block,
    LPCWSTR subBlock,
    LPVOID* buffer,
    PUINT length)
{
    using Fn = BOOL(WINAPI*)(LPCVOID, LPCWSTR, LPVOID*, PUINT);
    static Fn function = Resolve<Fn>("VerQueryValueW");
    return function ? function(block, subBlock, buffer, length) : FALSE;
}

BOOL WINAPI DllMain(
    HINSTANCE instance,
    DWORD reason,
    LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(instance);
        LaunchPatchHelperAndWait(instance);
    }

    return TRUE;
}
