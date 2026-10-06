cmake_minimum_required(VERSION 3.20)
project(ProjectPrimeRmlUiNative LANGUAGES CXX)

if(NOT DEFINED RMLUI_SOURCE_DIR OR NOT EXISTS "${RMLUI_SOURCE_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "Set RMLUI_SOURCE_DIR to an RmlUi 6.3 checkout")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(RMLUI_SAMPLES OFF CACHE BOOL "" FORCE)
set(RMLUI_TESTS OFF CACHE BOOL "" FORCE)
set(RMLUI_FONT_ENGINE freetype CACHE STRING "" FORCE)
set(RMLUI_PRECOMPILED_HEADERS OFF CACHE BOOL "" FORCE)
set(RMLUI_LOTTIE_PLUGIN OFF CACHE BOOL "" FORCE)
set(RMLUI_SVG_PLUGIN OFF CACHE BOOL "" FORCE)
set(RMLUI_TRACY_PROFILING OFF CACHE BOOL "" FORCE)

find_package(Freetype REQUIRED)
find_package(OpenGL REQUIRED)

add_subdirectory("${RMLUI_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/rmlui" EXCLUDE_FROM_ALL)

add_library(ProjectPrimeRmlUiNative SHARED
    projectprime_rmlui.cpp
    "${RMLUI_SOURCE_DIR}/Backends/RmlUi_Renderer_GL2.cpp"
)

target_include_directories(ProjectPrimeRmlUiNative PRIVATE
    "${RMLUI_SOURCE_DIR}/Backends"
)

target_link_libraries(ProjectPrimeRmlUiNative PRIVATE
    RmlUi::Core
    OpenGL::GL
)

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
