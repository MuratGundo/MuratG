cmake_minimum_required(VERSION 3.24)

project(
    GuardiansGameExeBootstrap
    VERSION 0.1.0
    LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "Bu proje yalnızca Windows içindir.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Guardians.exe x64 olduğu için x64 derleme seçilmelidir.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
    set(
        CMAKE_MSVC_RUNTIME_LIBRARY
        "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_library(
    guardians_game_bootstrap
    SHARED
    version_proxy.cpp
    version_proxy.def)

target_compile_definitions(
    guardians_game_bootstrap
    PRIVATE
        UNICODE
        _UNICODE
        WIN32_LEAN_AND_MEAN
        NOMINMAX)

target_compile_options(
    guardians_game_bootstrap
    PRIVATE
        /W4
        /EHsc
        /utf-8)

set_target_properties(
    guardians_game_bootstrap
    PROPERTIES
        OUTPUT_NAME "version"
        PREFIX "")
