# dist/ — 미리 빌드한 바이너리

| 파일 | 대상 | 비고 |
|---|---|---|
| `MacChanger-x64.exe` | 64비트 Windows (7 SP1 이상 예상) | GUI. 관리자 권한(UAC) 필요 |
| `MacChanger-x86.exe` | 32비트 Windows 전용 | 64비트 Windows 에서는 조회만 가능(ERROR_IN_WOW64) |
| `macchg_unit_tests-x64.exe` | 콘솔 | 가짜 백엔드 단위 테스트(관리자 권한 불필요) |
| `macchg_integration_tests-x64.exe` | 콘솔 | HKCU 격리 키 통합 테스트(관리자 권한 불필요, HKLM 미접근) |

빌드 도구: Ubuntu 24.04, mingw-w64 11.0.1 / GCC 13.2.0 (win32 스레드), CMake 3.28, Ninja 1.11. 정적 링크(libstdc++/libgcc 미의존).

**이 바이너리들은 Windows 에서 실행·검증되지 않았습니다** (빌드 환경에 Windows/Wine 없음). `docs/TESTING.md` 의 체크리스트로 검증 후 결과를 기록하세요.
