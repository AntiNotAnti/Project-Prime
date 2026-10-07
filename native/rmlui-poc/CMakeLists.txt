cmake_minimum_required(VERSION 3.20)
project(ProjectPrimeRmlUiNative LANGUAGES CXX)

if(NOT DEFINED RMLUI_SOURCE_DIR OR NOT EXISTS "${RMLUI_SOURCE_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "Set RMLUI_SOURCE_DIR to an RmlUi 6.3 checkout")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
option(PROJECT_PRIME_RMLUI_GL2 "Include the desktop compatibility OpenGL adapter" ON)
# /MT keeps the optional Windows bridge self-contained. It must match FreeType.
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(RMLUI_SAMPLES OFF CACHE BOOL "" FORCE)
set(RMLUI_TESTS OFF CACHE BOOL "" FORCE)
set(RMLUI_FONT_ENGINE freetype CACHE STRING "" FORCE)
set(RMLUI_PRECOMPILED_HEADERS OFF CACHE BOOL "" FORCE)
set(RMLUI_LOTTIE_PLUGIN OFF CACHE BOOL "" FORCE)
set(RMLUI_SVG_PLUGIN OFF CACHE BOOL "" FORCE)
set(RMLUI_TRACY_PROFILING OFF CACHE BOOL "" FORCE)

find_package(Freetype REQUIRED)
if(PROJECT_PRIME_RMLUI_GL2)
    if(ANDROID)
        message(FATAL_ERROR "The GL2 adapter is desktop-only. Android must set PROJECT_PRIME_RMLUI_GL2=OFF and consume the draw-list ABI.")
    endif()
    find_package(OpenGL REQUIRED)
endif()

add_subdirectory("${RMLUI_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/rmlui" EXCLUDE_FROM_ALL)

add_library(ProjectPrimeRmlUiNative SHARED
    projectprime_rmlui.cpp
    projectprime_rmlui_draw_list.cpp
)

if(PROJECT_PRIME_RMLUI_GL2)
    target_sources(ProjectPrimeRmlUiNative PRIVATE
        projectprime_rmlui_gl2.cpp
        "${RMLUI_SOURCE_DIR}/Backends/RmlUi_Renderer_GL2.cpp"
    )
    target_compile_definitions(ProjectPrimeRmlUiNative PRIVATE PP_RMLUI_GL2=1)
    target_include_directories(ProjectPrimeRmlUiNative PRIVATE "${RMLUI_SOURCE_DIR}/Backends")
    target_link_libraries(ProjectPrimeRmlUiNative PRIVATE OpenGL::GL)
endif()

target_link_libraries(ProjectPrimeRmlUiNative PRIVATE
    RmlUi::Core
)
if(ANDROID)
    # Android 15+ supports devices with 16 KB memory pages; native APK payloads
    # must be compatible even when they are cross-built on a 4 KB host.
    target_link_options(ProjectPrimeRmlUiNative PRIVATE "-Wl,-z,max-page-size=16384")
endif()

set_target_properties(ProjectPrimeRmlUiNative PROPERTIES
    OUTPUT_NAME "ProjectPrime.RmlUi.Native"
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)

if(APPLE)
    set_target_properties(ProjectPrimeRmlUiNative PROPERTIES
        BUILD_WITH_INSTALL_RPATH YES
        INSTALL_RPATH "@loader_path"
    )
endif()

install(TARGETS ProjectPrimeRmlUiNative
    LIBRARY DESTINATION .
    RUNTIME DESTINATION .
)
