cmake_minimum_required(VERSION 3.24)

project(
    GuardiansWinMMZipLoader
    VERSION 0.1.1
    LANGUAGES C CXX ASM_MASM)

if(NOT WIN32)
    message(FATAL_ERROR "Bu proje yalnızca Windows içindir.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Guardians.exe x64 olduğu için x64 derleme seçilmelidir.")
endif()

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
    set(
        CMAKE_MSVC_RUNTIME_LIBRARY
        "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

set(
    WINMM_FORWARDER_ASM
    "${CMAKE_CURRENT_SOURCE_DIR}/winmm_forwarders.generated.asm")

set(
    WINMM_EXPORT_DEF
    "${CMAKE_CURRENT_SOURCE_DIR}/winmm_exports.generated.def")

set(
    WINMMBASE_IMPORT_LIB
    "${CMAKE_CURRENT_SOURCE_DIR}/winmmbase_import.generated.lib")

foreach(REQUIRED_FILE
    "${WINMM_FORWARDER_ASM}"
    "${WINMM_EXPORT_DEF}"
    "${WINMMBASE_IMPORT_LIB}")
    if(NOT EXISTS "${REQUIRED_FILE}")
        message(
            FATAL_ERROR
            "${REQUIRED_FILE} bulunamadı. Önce GENERATE_WINMM_DEF.ps1 çalıştırılmalıdır.")
    endif()
endforeach()

include(FetchContent)

FetchContent_Declare(
    minhook_source
    GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
    GIT_TAG v1.3.4
    GIT_SHALLOW TRUE)

FetchContent_GetProperties(minhook_source)

if(NOT minhook_source_POPULATED)
    FetchContent_Populate(minhook_source)
endif()

add_library(
    minhook STATIC
    "${minhook_source_SOURCE_DIR}/src/buffer.c"
    "${minhook_source_SOURCE_DIR}/src/hook.c"
    "${minhook_source_SOURCE_DIR}/src/trampoline.c"
    "${minhook_source_SOURCE_DIR}/src/hde/hde64.c")

target_include_directories(
    minhook
    PUBLIC
        "${minhook_source_SOURCE_DIR}/include"
    PRIVATE
        "${minhook_source_SOURCE_DIR}/src"
        "${minhook_source_SOURCE_DIR}/src/hde")

FetchContent_Declare(
    miniz_source
    GIT_REPOSITORY https://github.com/richgel999/miniz.git
    GIT_TAG 3.1.2
    GIT_SHALLOW TRUE)

FetchContent_GetProperties(miniz_source)

if(NOT miniz_source_POPULATED)
    FetchContent_Populate(miniz_source)
endif()

set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(BUILD_FUZZERS OFF CACHE BOOL "" FORCE)
set(BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(INSTALL_PROJECT OFF CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(AMALGAMATE_SOURCES OFF CACHE BOOL "" FORCE)

add_subdirectory(
    "${miniz_source_SOURCE_DIR}"
    "${miniz_source_BINARY_DIR}"
    EXCLUDE_FROM_ALL)

add_library(
    guardians_winmm
    SHARED
    src/winmm_proxy.cpp
    "${WINMM_FORWARDER_ASM}"
    "${WINMM_EXPORT_DEF}")

target_compile_definitions(
    guardians_winmm
    PRIVATE
        UNICODE
        _UNICODE
        WIN32_LEAN_AND_MEAN
        NOMINMAX)

target_compile_options(
    guardians_winmm
    PRIVATE
        /W4
        /EHsc
        /utf-8)

target_link_libraries(
    guardians_winmm
    PRIVATE
        minhook
        miniz
        "${WINMMBASE_IMPORT_LIB}")

set_target_properties(
    guardians_winmm
    PROPERTIES
        OUTPUT_NAME "winmm"
        PREFIX "")

install(
    TARGETS guardians_winmm
    RUNTIME DESTINATION .)
