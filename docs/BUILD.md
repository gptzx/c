# 빌드 안내

## 요구 사항 요약

| 구성 | 도구 집합 | 실제 사용/검증 여부 |
|---|---|---|
| Windows x64 / x86 EXE (교차 빌드) | Ubuntu 24.04 `mingw-w64` 11.0.1, GCC 13.2.0 (`x86_64-w64-mingw32-g++`, `i686-w64-mingw32-g++`, win32 스레드 모델), CMake 3.28, Ninja 1.11 | **이 저장소의 `dist/` EXE 를 실제로 이 도구로 빌드함** |
| Windows x64 / x86 / ARM64 EXE (MSVC) | Visual Studio 2022 (17.x) v143 도구 집합, Windows 10 SDK (10.0.19041 이상 권장), CMake 3.20 이상 | 빌드 구성(`CMakePresets.json`)만 제공. 이 작업 환경(Linux)에서는 실행하지 않았음 |
| 코어 단위 테스트 (호스트) | GCC 13.3 또는 Clang 18, CMake, Ninja (Linux/macOS/Windows 모두 가능) | Linux 에서 실행, 50/50 통과 |

C++17 이 필요합니다. 외부 라이브러리 의존성은 없습니다(Windows SDK 만 사용).

## 1. MinGW-w64 교차 빌드 (Linux → Windows)

```bash
sudo apt-get install -y cmake ninja-build mingw-w64
cmake --preset mingw-x64 && cmake --build --preset mingw-x64
cmake --preset mingw-x86 && cmake --build --preset mingw-x86
```

출력:

- `build/mingw-x64/MacChanger.exe`, `build/mingw-x86/MacChanger.exe` — GUI (단일 EXE)
- `build/mingw-*/macchg_unit_tests.exe` — 단위 테스트(가짜 백엔드)
- `build/mingw-*/macchg_integration_tests.exe` — HKCU 격리 키 통합 테스트

링크 옵션 `-static -static-libgcc -static-libstdc++` 로 libstdc++/libgcc/winpthread DLL 에 의존하지 않습니다. 실제 임포트 확인:

```bash
x86_64-w64-mingw32-objdump -p build/mingw-x64/MacChanger.exe | grep "DLL Name"
# ADVAPI32.dll bcrypt.dll COMCTL32.dll comdlg32.dll GDI32.dll IPHLPAPI.DLL KERNEL32.dll msvcrt.dll SETUPAPI.dll USER32.dll
```

모두 Windows 7 SP1 이상에 기본 포함된 시스템 DLL 입니다(`msvcrt.dll` 은 MinGW CRT 가 사용하는 OS 내장 CRT). 별도 런타임 설치가 필요하지 않습니다.

## 2. MSVC 빌드 (Windows)

Visual Studio 2022 의 "C++ 데스크톱 개발" 워크로드(MSVC v143, Windows 10 SDK, CMake 도구 포함)가 필요합니다.

```bat
cmake --preset msvc-x64
cmake --build --preset msvc-x64
ctest --preset msvc-x64
```

x86 은 `msvc-x86`, ARM64 는 `msvc-arm64` 프리셋을 사용합니다.

- `CMAKE_MSVC_RUNTIME_LIBRARY = MultiThreaded` (`/MT`) 로 **정적 런타임** 링크 → VC++ 재배포 패키지가 필요하지 않습니다. 배포 전 `dumpbin /dependents MacChanger.exe` 로 `VCRUNTIME*.dll`, `MSVCP*.dll`, `api-ms-win-crt-*.dll` 이 없는지 확인하세요.
- 매니페스트(`src/gui/app.manifest`)는 리소스로 포함되며 링커 자동 매니페스트는 `/MANIFEST:NO` 로 끕니다.
- 서브시스템 최소 버전 `6.01`(Windows 7).
- Microsoft 의 "Visual C++ 지원 플랫폼" 문서에 따르면 VS 2022 로 빌드한 앱의 실행 대상은 Windows 7 SP1, 8.1, 10, 11(및 해당 서버)입니다. **Windows 8(8.1 이 아닌)** 은 공식 목록에 없습니다. Windows XP 는 별도의 `v141_xp` 도구 집합(VS 2017/2019 설치 프로그램 제공, 사용 중단됨)이 필요하며 이 프로젝트는 XP 빌드를 제공하지 않습니다.

## 3. 호스트 단위 테스트 (Linux/macOS)

```bash
cmake --preset host-tests && cmake --build --preset host-tests
./build/host-tests/macchg_unit_tests        # 또는 ctest --preset host-tests
```

코어(`src/core`)는 Windows 헤더에 의존하지 않으므로 어떤 호스트에서도 컴파일·실행됩니다. 테스트는 가짜 레지스트리/장치/난수 백엔드(`tests/unit/fakes.h`)만 사용합니다.

## 4. Windows 통합 테스트

```bat
build\msvc-x64\Release\macchg_integration_tests.exe
```

`HKCU\Software\MacChangerTest\run-<pid>-<tick>` 아래에 격리된 키를 만들어 실제 `RegEnumValueW` 등 레지스트리 API 동작(기본값, 긴 값, 16000자 이름, REG_QWORD/REG_DWORD_BIG_ENDIAN/알 수 없는 자료형, 널 종결 없는 REG_SZ)을 검증한 뒤 키를 삭제합니다. HKLM 네트워크 설정은 건드리지 않으며 관리자 권한도 필요하지 않습니다. **이 작업 환경에는 Windows/Wine 이 없어 실행하지 못했습니다(빌드만 확인).**

## 5. 컴파일 정의

- `UNICODE`, `_UNICODE`
- `_WIN32_WINNT=0x0601`, `WINVER=0x0601`, `NTDDI_VERSION=0x06010000` — Windows 7 보다 새로운 API 를 정적으로 사용하지 않도록 고정. 더 새로운 API(`IsWow64Process2`, `TaskDialogIndirect`)는 `GetProcAddress` 로 동적 호출하고 폴백을 둡니다.
- MSVC: `/W4 /permissive- /EHsc /utf-8 /Zc:__cplusplus`
- GCC/MinGW: `-Wall -Wextra -Wpedantic -Wshadow -finput-charset=UTF-8`
