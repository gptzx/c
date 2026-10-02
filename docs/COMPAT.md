# 지원 범위와 호환성

## 용어

- **검증됨**: 실제 해당 환경에서 실행하고 결과를 확인함.
- **빌드됨/미검증**: 바이너리는 만들었지만 그 환경에서 실행·확인하지 않음.
- **미지원**: 빌드하지 않았고 동작을 주장하지 않음.

> 이 저장소의 모든 Windows 실행 검증은 **아직 수행되지 않았습니다.** 작업 환경이 Linux 컨테이너(Wine 없음)였기 때문입니다. 아래 표의 "예상"은 사용 API 의 문서화된 최소 OS 를 근거로 한 판단이며, 실행 결과가 아닙니다. 자세한 내용은 `docs/TESTING.md`.

## OS × 아키텍처 × 빌드

| OS | x64 EXE (MinGW, `dist/`) | x86 EXE (MinGW, `dist/`) | MSVC x64/x86 | MSVC ARM64 |
|---|---|---|---|---|
| Windows 11 | 빌드됨/미검증 (실행 예상) | 64비트 OS 에서는 **조회만** 가능(아래 참고) | 구성만 제공 | 구성만 제공, 미검증 |
| Windows 10 | 빌드됨/미검증 (실행 예상) | 32비트 Win10 에서 실행 예상, 미검증 | 구성만 제공 | 해당 없음 |
| Windows 8.1 | 빌드됨/미검증 (실행 예상) | 32비트 8.1 에서 실행 예상, 미검증 | 구성만 제공(MS 공식 지원 대상) | 해당 없음 |
| Windows 8 | 빌드됨/미검증 (실행 예상) | 동일 | VS 2022 공식 지원 목록에 없음 | 해당 없음 |
| Windows 7 SP1 | 빌드됨/미검증 (실행 예상, 아래 API 근거) | 동일 | 구성만 제공(MS 공식 지원 대상, 정적 CRT) | 해당 없음 |
| Windows Vista | 미지원 (일부 API 는 존재하나 검증·지원 표시 안 함) | 동일 | 미지원 | 해당 없음 |
| Windows XP | 미지원 (별도 레거시 빌드 필요, 미구현) | 동일 | 미지원 | 해당 없음 |

### x86 빌드와 64비트 Windows

`SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)` 는 WOW64(64비트 OS 의 32비트 프로세스)에서 `ERROR_IN_WOW64` 로 실패합니다. 이 프로그램은 시작 시 `IsWow64Process` 로 이를 감지하면 **변경/원상복구 버튼을 비활성화하고 조회만 허용**합니다. 64비트 Windows 에서는 x64 빌드를 사용하세요. x86 빌드는 32비트 Windows 전용입니다.

### ARM64

- `msvc-arm64` 프리셋으로 네이티브 ARM64 빌드 구성을 제공하지만 **빌드·실행 모두 미검증**입니다.
- x64 EXE 를 ARM64 Windows 11 의 x64 에뮬레이션으로 실행하는 경우 장치 제어가 실패할 가능성이 있습니다(`IsWow64Process2` 로 감지되면 경고를 표시). 미검증.
- MinGW-w64 로는 ARM64 빌드를 만들지 않았습니다(llvm-mingw 등 별도 도구 집합 필요).

## 사용 API 와 최소 OS

| API | 용도 | 최소 OS |
|---|---|---|
| `GetAdaptersAddresses` (IP Helper) | 인터페이스 GUID/이름/IfType/연결 상태/현재 MAC | XP SP1 (사용 플래그 기준 Vista) |
| `SetupDiGetClassDevs`, `SetupDiOpenDevRegKey`, `SetupDiGetDeviceRegistryProperty`, `SetupDiGetDeviceInstanceId`, `SetupDiSetClassInstallParams`, `SetupDiCallClassInstaller`, `SetupDiGetDeviceInstallParams` | 장치 ↔ 드라이버 키 대응, 중지/재활성화, 재부팅 필요 플래그 | 2000/XP |
| `CM_Get_DevNode_Status` | 장치 사용/사용 안 함(CM_PROB_DISABLED) 판정 | 2000/XP |
| `RegOpenKeyExW`(KEY_WOW64_64KEY), `RegQueryInfoKeyW`, `RegEnumValueW`, `RegQueryValueExW`, `RegSetValueExW`, `RegDeleteValueW` | NetworkAddress 및 TCP/IP 값 백업/삭제/복원 | XP (WOW64 플래그는 XP x64/2003) |
| `BCryptGenRandom(BCRYPT_USE_SYSTEM_PREFERRED_RNG)` | 암호학적 난수 | **Vista** |
| `GetTickCount64` | 시간 초과 계산 | **Vista** |
| `DeviceIoControl(IOCTL_NDIS_QUERY_GLOBAL_STATS)` + `OID_802_3_PERMANENT_ADDRESS` | 영구 MAC 조회(실패 시 "확인 불가") | XP |
| `TaskDialogIndirect` (동적 호출, 폴백 MessageBox) | 종료 시 3지선다 | Vista(없으면 폴백) |
| `IsWow64Process` / `IsWow64Process2` (동적) | 아키텍처 감지 | XP SP2 / Win10 1511 (없으면 생략) |
| `RegDeleteTreeW` (통합 테스트에서만) | 격리 키 정리 | Vista |

Vista 전용 API(BCrypt, GetTickCount64)가 정적으로 링크되어 있으므로 **XP 에서는 실행되지 않습니다.** XP/Vista 레거시 빌드를 만들려면 `CryptGenRandom`/`GetTickCount` 대체, 서브시스템 버전 5.1, XP 를 지원하는 도구 집합(v141_xp 또는 구형 MinGW)이 필요합니다. 이 저장소는 이를 구현·검증하지 않았으며 지원한다고 표시하지 않습니다.

## 어댑터/드라이버 호환성

- **유선랜**: 대부분의 NDIS 6 미니포트가 `NetworkAddress` 를 읽어 적용합니다(Microsoft `NdisReadNetworkAddress` 문서 및 HLK "AddressChange" 테스트가 이 동작을 요구). 그러나 드라이버가 값을 무시할 수 있으며 이 경우 프로그램은 "적용 실패(지원하지 않음)"로 표시하고 변경 전 상태로 되돌립니다.
- **무선랜**: 많은 Wi-Fi 드라이버가 첫 바이트가 `02` 인 값만 받아들이거나 전혀 받아들이지 않습니다. "첫 바이트 02 고정" 옵션을 제공하지만 모든 무선랜의 변경을 보장하지 않습니다. 지원 여부는 사전에 알 수 없으므로 UI 에 "미확인"으로 표시하고 실제 적용 결과로 판정합니다.
- **Windows Wi-Fi 임의 하드웨어 주소**(Windows 10 이상 설정): 이 프로그램은 `HKLM\SOFTWARE\Microsoft\WlanSvc\Interfaces\{GUID}\RandomMacState` 를 **읽기만** 하여 "켜짐/꺼짐으로 추정"을 상세 정보에 표시합니다. 이 설정을 변경하지 않습니다. 켜져 있으면 사용자가 지정한 MAC 과 다른 결과가 나올 수 있습니다.
- **실제 검증한 어댑터 조합: 없음.** 유선/무선 어댑터 모델별 결과는 `docs/TESTING.md` 의 체크리스트로 확인해 기록하세요.
