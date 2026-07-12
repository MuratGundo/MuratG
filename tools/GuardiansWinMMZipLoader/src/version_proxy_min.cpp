#define WIN32_LEAN_AND_MEAN
#define NOMINMAX

#include <windows.h>

#include <string>
#include <vector>

namespace
{
    HMODULE g_thisModule = nullptr;
    HMODULE g_realVersion = nullptr;
    INIT_ONCE g_versionOnce = INIT_ONCE_STATIC_INIT;

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
