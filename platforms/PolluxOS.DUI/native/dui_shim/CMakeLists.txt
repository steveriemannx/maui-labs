cmake_minimum_required(VERSION 3.20)

# dui_shim — C ABI bridge between the DUI C++ toolkit and the managed
# PolluxOS.DUI MAUI backend.
#
# Consumes an installed DUI tree (produced by scripts/build-dui-macos.sh):
#
#   cmake -S native/dui_shim -B native/dui_shim/build \
#         -DCMAKE_BUILD_TYPE=Release \
#         -DCMAKE_PREFIX_PATH=<dui-install-prefix>
#   cmake --build native/dui_shim/build

project(dui_shim VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(APPLE)
  if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
    set(CMAKE_OSX_DEPLOYMENT_TARGET "14.0" CACHE STRING "" FORCE)
  endif()
  message(STATUS "dui_shim: macOS deployment target ${CMAKE_OSX_DEPLOYMENT_TARGET} "
                 "(DUI must be built with the same target — scripts/build-dui-macos.sh pins it)")
endif()

find_package(dui CONFIG REQUIRED)

if(APPLE)
  # The macOS build does not need DUI's Linux/FreeBSD dependencies, yet the installed
  # target still carries them on its interface:
  #   * absolute Homebrew X11/Xext dylib paths (would be a runtime requirement),
  #   * Homebrew FreeType/Fontconfig (macOS text goes through CoreText).
  # Left alone, the bridge — and every app consuming it — would depend on a Homebrew
  # install. Strip those entries; if the archives really referenced them the link
  # would fail with undefined symbols (it does not: verified on macOS 26 / arm64).
  get_target_property(_dui_interface dui::dui INTERFACE_LINK_LIBRARIES)
  if(_dui_interface)
    set(_dui_interface_filtered "")
    foreach(_dui_link IN LISTS _dui_interface)
      if(_dui_link MATCHES "(^|/)libX(11|ext)" OR _dui_link MATCHES "Freetype|Fontconfig|freetype|fontconfig")
        message(STATUS "dui_shim: dropping ${_dui_link} from dui::dui (macOS build)")
      else()
        list(APPEND _dui_interface_filtered "${_dui_link}")
      endif()
    endforeach()
    set_target_properties(dui::dui PROPERTIES INTERFACE_LINK_LIBRARIES "${_dui_interface_filtered}")
  endif()
endif()

add_library(dui_shim SHARED
  src/dui_shim.cpp
)

target_include_directories(dui_shim
  PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(dui_shim PRIVATE dui::dui)

target_compile_definitions(dui_shim PRIVATE DUI_SHIM_BUILD=1)

if(MSVC)
  target_compile_options(dui_shim PRIVATE /W4)
else()
  target_compile_options(dui_shim PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()

# The shim dlopens nothing itself; keep the runtime search path confined to the
# directory the dylib is loaded from so the .NET app can ship DUI next to it.
if(APPLE)
  set_target_properties(dui_shim PROPERTIES
    INSTALL_RPATH "@loader_path"
    BUILD_WITH_INSTALL_RPATH OFF)
elseif(UNIX)
  set_target_properties(dui_shim PROPERTIES
    INSTALL_RPATH "$ORIGIN"
    BUILD_WITH_INSTALL_RPATH OFF)
endif()

install(TARGETS dui_shim
  LIBRARY DESTINATION lib
  RUNTIME DESTINATION bin
  ARCHIVE DESTINATION lib)
install(FILES include/dui_shim.h DESTINATION include)
