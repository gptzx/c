cmake_minimum_required(VERSION 3.20)
# 정적 MSVC 런타임(/MT) 선택을 위해 CMP0091 NEW 가 필요하다.
cmake_policy(SET CMP0091 NEW)
project(MacChanger VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(MACCHG_BUILD_GUI "Windows GUI 실행 파일 빌드" ${WIN32})
option(MACCHG_BUILD_TESTS "단위 테스트 빌드" ON)
option(MACCHG_BUILD_INTEGRATION_TESTS "Windows 격리 레지스트리 키 통합 테스트 빌드" ${WIN32})

# ---------------------------------------------------------------- 공통 컴파일 옵션
if(MSVC)
  # 정적 런타임: 배포 시 VC++ 재배포 패키지 불필요
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_compile_options(/W4 /permissive- /EHsc /utf-8 /Zc:__cplusplus)
  add_compile_definitions(_CRT_SECURE_NO_WARNINGS)
else()
  add_compile_options(-Wall -Wextra -Wpedantic -Wshadow -finput-charset=UTF-8)
endif()

if(WIN32)
  # 최소 대상 OS: Windows 7 SP1. 그보다 새로운 API 는 사용하지 않는다.
  add_compile_definitions(UNICODE _UNICODE _WIN32_WINNT=0x0601 WINVER=0x0601 NTDDI_VERSION=0x06010000 WIN32_LEAN_AND_MEAN NOMINMAX)
endif()

# ---------------------------------------------------------------- 코어(플랫폼 독립)
add_library(macchg_core STATIC
  src/core/utf.cpp
  src/core/registry_types.cpp
  src/core/mac_address.cpp
  src/core/backup.cpp
  src/core/cleanup.cpp
  src/core/engine.cpp
  src/core/restore_file.cpp
  src/core/describe_ko.cpp
)
target_include_directories(macchg_core PUBLIC src)

# ---------------------------------------------------------------- Windows 백엔드 + GUI
if(WIN32)
  add_library(macchg_win STATIC
    src/win/win_registry.cpp
    src/win/win_adapters.cpp
    src/win/win_device.cpp
    src/win/win_random.cpp
    src/win/win_system.cpp
  )
  target_link_libraries(macchg_win PUBLIC macchg_core iphlpapi setupapi cfgmgr32 bcrypt advapi32 ws2_32)

  if(MACCHG_BUILD_GUI)
    add_executable(MacChanger WIN32
      src/gui/main.cpp
      src/gui/app.rc
    )
    target_link_libraries(MacChanger PRIVATE macchg_win comctl32 user32 gdi32 shell32 comdlg32 ole32 uxtheme)
    if(MSVC)
      # 매니페스트는 .rc 로 포함하므로 링커 생성 매니페스트는 끈다.
      target_link_options(MacChanger PRIVATE /MANIFEST:NO)
      # 서브시스템 최소 버전 6.01 = Windows 7
      target_link_options(MacChanger PRIVATE "/SUBSYSTEM:WINDOWS,6.01")
    else()
      # MinGW: 완전 정적 링크(libgcc/libstdc++/winpthread 미의존), 유니코드 진입점
      target_link_options(MacChanger PRIVATE -static -static-libgcc -static-libstdc++ -municode -mwindows -Wl,--major-subsystem-version,6 -Wl,--minor-subsystem-version,1)
      target_compile_options(MacChanger PRIVATE -municode)
    endif()
    set_target_properties(MacChanger PROPERTIES OUTPUT_NAME "MacChanger")
  endif()
endif()

# ---------------------------------------------------------------- 단위 테스트(가짜 백엔드, 호스트에서 실행 가능)
if(MACCHG_BUILD_TESTS)
  enable_testing()
  add_executable(macchg_unit_tests
    tests/unit/test_main.cpp
    tests/unit/test_mac_address.cpp
    tests/unit/test_cleanup.cpp
    tests/unit/test_engine_change.cpp
    tests/unit/test_engine_restore.cpp
    tests/unit/test_restore_file.cpp
  )
  target_link_libraries(macchg_unit_tests PRIVATE macchg_core)
  target_include_directories(macchg_unit_tests PRIVATE tests/unit)
  if(WIN32 AND NOT MSVC)
    target_link_options(macchg_unit_tests PRIVATE -static -static-libgcc -static-libstdc++)
  endif()
  add_test(NAME unit COMMAND macchg_unit_tests)
endif()

# ---------------------------------------------------------------- Windows 통합 테스트(HKCU 격리 키)
if(WIN32 AND MACCHG_BUILD_INTEGRATION_TESTS)
  add_executable(macchg_integration_tests
    tests/integration/win_registry_integration_test.cpp
  )
  target_link_libraries(macchg_integration_tests PRIVATE macchg_win)
  target_include_directories(macchg_integration_tests PRIVATE tests/unit)
  if(NOT MSVC)
    # wmain 진입점을 쓰므로 MinGW 에서는 -municode 가 필요하다.
    target_compile_options(macchg_integration_tests PRIVATE -municode)
    target_link_options(macchg_integration_tests PRIVATE -static -static-libgcc -static-libstdc++ -municode)
  endif()
  add_test(NAME integration_registry COMMAND macchg_integration_tests)
endif()
