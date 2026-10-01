# fifastreet - ReXGlue Recompiled Project
#
# SDK boilerplate lives in generated/rexglue.cmake.

cmake_minimum_required(VERSION 3.25)
project(fifastreet LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(generated/rexglue.cmake)

# Sources
set(FIFASTREET_SOURCES
    src/main.cpp
)

if(WIN32)
    enable_language(RC)
    add_executable(fifastreet WIN32 ${FIFASTREET_SOURCES})
    target_sources(fifastreet PRIVATE src/fifastreet.rc)
else()
    add_executable(fifastreet ${FIFASTREET_SOURCES})
endif()

rexglue_setup_target(fifastreet GPU_PLUGINS xenos)
target_include_directories(fifastreet PRIVATE src)

# The source SDK keeps ImGui private to rexruntime; its public UI headers
# still require the include directory when compiling the host application.
# Propagate headers only, keeping the ImGui objects in the shared runtime.
if(TARGET imgui::imgui)
    target_include_directories(fifastreet PRIVATE
        $<TARGET_PROPERTY:imgui::imgui,INTERFACE_INCLUDE_DIRECTORIES>)
endif()

# Guest SIMD code must be optimized even when using the debug runtime. Keep
# debug symbols and checked runtime libraries while avoiding unoptimized SIMD
# wrappers in the game and its separately compiled DLL.
if(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
    target_compile_options(fifastreet PRIVATE -O2)
    if(TARGET fifastreet_recomp)
        target_compile_options(fifastreet_recomp PRIVATE -O2)
    endif()
endif()

# FIFA Street DLL-specific kernel stubs.
if(TARGET fifastreet_fifadllzf_xex)
    if(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
        target_compile_options(fifastreet_fifadllzf_xex PRIVATE -O2)
    endif()
    target_sources(fifastreet_fifadllzf_xex PRIVATE
        src/stubs.cpp
    )
endif()
