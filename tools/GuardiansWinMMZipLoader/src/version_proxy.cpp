#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>

#include <array>
#include <string>
#include <vector>

namespace
{
    HMODULE g_thisModule = nullptr;
    HMODULE g_realVersion = nullptr;
    INIT_ONCE g_versionOnce = INIT_ONCE_STATIC_INIT;

    BOOL CALLBACK LoadRealVersion(
        PINIT_ONCE,
        PVOID,
        PVOID*)
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

    std::wstring GetModuleDirectory(HMODULE module)
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

        std::wstring path(buffer.data(), length);
        const std::size_t slash = path.find_last_of(L"\\/");

        if (slash == std::wstring::npos)
        {
            return {};
        }

        return path.substr(0, slash);
    }

    DWORD WINAPI LoadUCrewLoaderThread(LPVOID)
    {
        const std::wstring directory = GetModuleDirectory(g_thisModule);

        if (directory.empty())
        {
            return 1;
        }

        const std::wstring loaderPath =
            directory + L"\\ucrew_loader.dll";

        LoadLibraryW(loaderPath.c_str());
        return 0;
    }
}

extern "C" __declspec(dllexport)
DWORD WINAPI GetFileVersionInfoSizeA(
    LPCSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(LPCSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(filename, handle);
}

extern "C" __declspec(dllexport)
DWORD WINAPI GetFileVersionInfoSizeW(
    LPCWSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(LPCWSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(filename, handle);
}

extern "C" __declspec(dllexport)
DWORD WINAPI GetFileVersionInfoSizeExA(
    DWORD flags,
    LPCSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(DWORD, LPCSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeExA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(flags, filename, handle);
}

extern "C" __declspec(dllexport)
DWORD WINAPI GetFileVersionInfoSizeExW(
    DWORD flags,
    LPCWSTR filename,
    LPDWORD handle)
{
    using Fn = DWORD(WINAPI*)(DWORD, LPCWSTR, LPDWORD);
    static Fn function = Resolve<Fn>("GetFileVersionInfoSizeExW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(flags, filename, handle);
}

extern "C" __declspec(dllexport)
BOOL WINAPI GetFileVersionInfoA(
    LPCSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(LPCSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }

    return function(filename, handle, length, data);
}

extern "C" __declspec(dllexport)
BOOL WINAPI GetFileVersionInfoW(
    LPCWSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(LPCWSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }

    return function(filename, handle, length, data);
}

extern "C" __declspec(dllexport)
BOOL WINAPI GetFileVersionInfoExA(
    DWORD flags,
    LPCSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(DWORD, LPCSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoExA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }

    return function(flags, filename, handle, length, data);
}

extern "C" __declspec(dllexport)
BOOL WINAPI GetFileVersionInfoExW(
    DWORD flags,
    LPCWSTR filename,
    DWORD handle,
    DWORD length,
    LPVOID data)
{
    using Fn = BOOL(WINAPI*)(DWORD, LPCWSTR, DWORD, DWORD, LPVOID);
    static Fn function = Resolve<Fn>("GetFileVersionInfoExW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }

    return function(flags, filename, handle, length, data);
}

extern "C" __declspec(dllexport)
BOOL WINAPI VerQueryValueA(
    LPCVOID block,
    LPCSTR subBlock,
    LPVOID* buffer,
    PUINT length)
{
    using Fn = BOOL(WINAPI*)(LPCVOID, LPCSTR, LPVOID*, PUINT);
    static Fn function = Resolve<Fn>("VerQueryValueA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }

    return function(block, subBlock, buffer, length);
}

extern "C" __declspec(dllexport)
BOOL WINAPI VerQueryValueW(
    LPCVOID block,
    LPCWSTR subBlock,
    LPVOID* buffer,
    PUINT length)
{
    using Fn = BOOL(WINAPI*)(LPCVOID, LPCWSTR, LPVOID*, PUINT);
    static Fn function = Resolve<Fn>("VerQueryValueW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return FALSE;
    }

    return function(block, subBlock, buffer, length);
}

extern "C" __declspec(dllexport)
DWORD WINAPI VerLanguageNameA(
    DWORD language,
    LPSTR buffer,
    DWORD size)
{
    using Fn = DWORD(WINAPI*)(DWORD, LPSTR, DWORD);
    static Fn function = Resolve<Fn>("VerLanguageNameA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(language, buffer, size);
}

extern "C" __declspec(dllexport)
DWORD WINAPI VerLanguageNameW(
    DWORD language,
    LPWSTR buffer,
    DWORD size)
{
    using Fn = DWORD(WINAPI*)(DWORD, LPWSTR, DWORD);
    static Fn function = Resolve<Fn>("VerLanguageNameW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(language, buffer, size);
}

extern "C" __declspec(dllexport)
DWORD WINAPI VerFindFileA(
    DWORD flags,
    LPCSTR filename,
    LPCSTR windowsDirectory,
    LPCSTR appDirectory,
    LPSTR currentDirectory,
    PUINT currentDirectoryLength,
    LPSTR destinationDirectory,
    PUINT destinationDirectoryLength)
{
    using Fn = DWORD(WINAPI*)(
        DWORD,
        LPCSTR,
        LPCSTR,
        LPCSTR,
        LPSTR,
        PUINT,
        LPSTR,
        PUINT);

    static Fn function = Resolve<Fn>("VerFindFileA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(
        flags,
        filename,
        windowsDirectory,
        appDirectory,
        currentDirectory,
        currentDirectoryLength,
        destinationDirectory,
        destinationDirectoryLength);
}

extern "C" __declspec(dllexport)
DWORD WINAPI VerFindFileW(
    DWORD flags,
    LPCWSTR filename,
    LPCWSTR windowsDirectory,
    LPCWSTR appDirectory,
    LPWSTR currentDirectory,
    PUINT currentDirectoryLength,
    LPWSTR destinationDirectory,
    PUINT destinationDirectoryLength)
{
    using Fn = DWORD(WINAPI*)(
        DWORD,
        LPCWSTR,
        LPCWSTR,
        LPCWSTR,
        LPWSTR,
        PUINT,
        LPWSTR,
        PUINT);

    static Fn function = Resolve<Fn>("VerFindFileW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(
        flags,
        filename,
        windowsDirectory,
        appDirectory,
        currentDirectory,
        currentDirectoryLength,
        destinationDirectory,
        destinationDirectoryLength);
}

extern "C" __declspec(dllexport)
DWORD WINAPI VerInstallFileA(
    DWORD flags,
    LPCSTR sourceFilename,
    LPCSTR destinationFilename,
    LPCSTR sourceDirectory,
    LPCSTR destinationDirectory,
    LPCSTR currentDirectory,
    LPSTR temporaryFile,
    PUINT temporaryFileLength)
{
    using Fn = DWORD(WINAPI*)(
        DWORD,
        LPCSTR,
        LPCSTR,
        LPCSTR,
        LPCSTR,
        LPCSTR,
        LPSTR,
        PUINT);

    static Fn function = Resolve<Fn>("VerInstallFileA");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(
        flags,
        sourceFilename,
        destinationFilename,
        sourceDirectory,
        destinationDirectory,
        currentDirectory,
        temporaryFile,
        temporaryFileLength);
}

extern "C" __declspec(dllexport)
DWORD WINAPI VerInstallFileW(
    DWORD flags,
    LPCWSTR sourceFilename,
    LPCWSTR destinationFilename,
    LPCWSTR sourceDirectory,
    LPCWSTR destinationDirectory,
    LPCWSTR currentDirectory,
    LPWSTR temporaryFile,
    PUINT temporaryFileLength)
{
    using Fn = DWORD(WINAPI*)(
        DWORD,
        LPCWSTR,
        LPCWSTR,
        LPCWSTR,
        LPCWSTR,
        LPCWSTR,
        LPWSTR,
        PUINT);

    static Fn function = Resolve<Fn>("VerInstallFileW");

    if (function == nullptr)
    {
        SetLastError(ERROR_PROC_NOT_FOUND);
        return 0;
    }

    return function(
        flags,
        sourceFilename,
        destinationFilename,
        sourceDirectory,
        destinationDirectory,
        currentDirectory,
        temporaryFile,
        temporaryFileLength);
}

BOOL WINAPI DllMain(
    HINSTANCE instance,
    DWORD reason,
    LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_thisModule = instance;
        DisableThreadLibraryCalls(instance);

        HANDLE thread = CreateThread(
            nullptr,
            0,
            LoadUCrewLoaderThread,
            nullptr,
            0,
            nullptr);

        if (thread != nullptr)
        {
            CloseHandle(thread);
        }
    }

    return TRUE;
}
