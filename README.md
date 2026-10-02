# MacChanger — Windows용 포터블 MAC 주소 변경 도구 (C++ / Win32)

TMAC 과 비슷한 흐름("어댑터 선택 → 랜덤 생성 → 변경")으로 네트워크 어댑터의 MAC 주소를 바꾸고, 변경과 함께 해당 어댑터의 TCP/IP 인터페이스 레지스트리 값(`EnableDHCP` 제외)을 정리하며, 변경 전 상태로 되돌릴 수 있는 한국어 GUI 프로그램입니다.

- **네이티브 C++17 / Win32 GUI**, 단일 EXE. .NET·Python·Electron·VC++ 재배포 패키지 설치가 필요 없습니다.
- 문서화된 Windows API 만 사용: IP Helper(`GetAdaptersAddresses`), SetupAPI(장치 식별·중지·재활성화), 레지스트리 API, BCrypt(암호학적 난수). PowerShell·WMIC·외부 실행 파일·커널 드라이버·펌웨어 변경을 사용하지 않습니다.
- **포터블**: 설치·서비스·예약 작업·자동 실행·앱 전용 레지스트리·AppData 설정·자동 로그·사용 이력·텔레메트리·네트워크 통신을 만들지 않습니다. 복구 데이터는 메모리에만 있습니다(필요하면 사용자가 직접 복구 파일 내보내기).
- **정직한 결과 표시**: 레지스트리 기록만으로 "성공"이라 하지 않고, 재활성화 후 실제 MAC 을 다시 조회해 일치할 때만 "변경 완료"로 표시합니다. 드라이버가 값을 무시하면 "적용 실패(지원하지 않음)"으로 표시하고 되돌립니다. MAC 적용 성공과 인터넷 연결은 별개로 표시합니다.

> **검증 상태 (중요)**: 이 저장소의 코어 로직은 가짜 백엔드로 50개 단위 테스트를 통과했고, Windows x64/x86 EXE 를 실제로 빌드했습니다. 그러나 작업 환경이 Linux 였기 때문에 **Windows 에서의 실행, 실물 유선랜·무선랜에서의 MAC 변경, OS 버전별 동작은 아직 검증되지 않았습니다.** 자세한 내용은 [docs/TESTING.md](docs/TESTING.md), [docs/COMPAT.md](docs/COMPAT.md).

## 다운로드 / 빌드

- `dist/MacChanger-x64.exe` — 64비트 Windows 용 (권장)
- `dist/MacChanger-x86.exe` — 32비트 Windows 전용 (64비트 Windows 에서는 조회만 가능)
- `dist/SHA256SUMS.txt`

이 EXE 들은 Ubuntu 24.04 의 MinGW-w64(GCC 13.2) 로 교차 빌드되었으며 시스템 DLL(`kernel32`, `advapi32`, `setupapi`, `iphlpapi`, `bcrypt`, `comctl32`, `comdlg32`, `user32`, `gdi32`, `msvcrt`)만 임포트합니다. MSVC(VS 2022, 정적 런타임 `/MT`) 빌드 구성도 제공합니다. 빌드 방법은 [docs/BUILD.md](docs/BUILD.md).

ARM64: MSVC 프리셋(`msvc-arm64`)만 제공하며 빌드·실행 미검증. 자세한 지원 범위는 [docs/COMPAT.md](docs/COMPAT.md).

## 사용 방법

1. `MacChanger-x64.exe` 를 실행합니다. UAC 승격 창이 뜨면 허용합니다(매니페스트 `requireAdministrator`).
2. 목록에서 어댑터를 선택합니다. 기본 목록에는 **물리 유선랜·무선랜**만 표시됩니다. "가상·기타 어댑터도 표시"를 켜면 VPN/터널/루프백/가상 어댑터도 `(가상)`/`(식별 불가)` 표시와 함께 보입니다. "상세 정보"(또는 더블클릭)에서 GUID, 장치 인스턴스 ID, 드라이버 키·버전, 영구 MAC(읽지 못하면 "확인 불가"), 현재 `NetworkAddress` 값, 무선랜의 Windows 임의 하드웨어 주소 설정(추정)을 볼 수 있습니다.
3. **랜덤 생성**: 운영체제 난수로 6바이트를 만들고 첫 바이트에 `(b & 0xFC) | 0x02` 를 적용합니다(두 번째 16진 문자는 항상 2/6/A/E, 유니캐스트·로컬 관리). 현재 MAC 과 이 PC 의 다른 어댑터 MAC 과 겹치면 다시 생성합니다(네트워크 전체 중복까지는 보장하지 않음). 무선랜용 "첫 바이트 02 고정" 옵션이 있습니다(모든 무선랜의 변경을 보장하지는 않음). 직접 입력도 가능합니다(`0A1B2C3D4E5F` 또는 `0A:1B:2C:3D:4E:5F`).
4. **변경**: 다음을 한 번에 실행합니다. 진행 상태가 "검증 중 → 백업 중 → 적용 중(어댑터 중지 / NetworkAddress 기록 / TCP/IP 정리 / 재활성화) → 확인 중 → 완료"로 표시되고, 변경 중 해당 어댑터의 네트워크가 잠시 끊깁니다.
   1. 대상·권한·입력 검증 (GUID ↔ 장치 인스턴스 ID ↔ 드라이버 키 `####` 가 1:1 로 확정되지 않으면 시작하지 않음)
   2. 변경 전 상태 백업 (`NetworkAddress` 존재 여부·자료형·원시 데이터, TCP/IP 인터페이스 키의 모든 직접 값, 어댑터 활성화 상태, 현재 MAC). 백업이 안정적으로 확보되지 않으면 시작하지 않음
   3. 선택한 어댑터만 중지 (SetupAPI `DICS_DISABLE`)
   4. `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}\####\NetworkAddress` 에 구분자 없는 12자리 `REG_SZ` 기록
   5. `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` 에서 **`EnableDHCP`(대소문자 무시)만 제외한 모든 직접 값 삭제**(이름 없는 기본값 포함, 하위 키·키 자체는 유지) 후 재열거로 검증. `EnableDHCP` 는 자료형·데이터 그대로 두고, 없었다면 만들지 않으며, `0` 이어도 바꾸지 않습니다. 고정 IP/DNS 가 삭제 대상이면 변경 전에 확인창으로 알립니다.
   6. 어댑터 재활성화 (`DICS_ENABLE`) — 원래 비활성이던 어댑터는 그대로 둠
   7. 실제 현재 MAC 을 다시 조회해 요청값과 비교, 연결 상태를 별도로 표시
5. **결과 해석**
   - `변경 완료`: 실제 MAC 이 요청값과 일치.
   - `재부팅 필요`: 장치가 재부팅 필요 플래그를 보고. 레지스트리에는 기록됨, 실제 적용은 재부팅 후 확인.
   - `적용 실패(지원하지 않음)`: 드라이버가 값을 무시. 변경 전 상태로 자동 복구됨. 무선랜이면 "첫 바이트 02 고정"으로 재시도.
   - `기록 완료(미확인)`: 어댑터가 원래 비활성이어서 실제 MAC 을 확인할 수 없음.
   - `확인 불가(시간 초과)`: 재활성화 후 제한 시간 안에 어댑터가 나타나지 않음. 변경 사항은 유지. "새로 고침" 후 필요하면 원상복구.
   - `실패 — 복구됨` / `실패 — 복구 실패`: 실패 단계, Windows 오류 코드, 남은 변경 사항과 복구 방법을 결과 창에 표시.
6. **원상복구**: 이 실행 세션에서 그 어댑터를 **처음 변경하기 직전 상태**로 되돌립니다. 여러 번 랜덤 변경해도 기준은 덮어쓰지 않습니다. 기존 `NetworkAddress` 가 있었다면 그대로 복원하고 없었을 때만 삭제하며, 삭제했던 TCP/IP 값을 모두(자료형·원시 데이터 그대로) 복원합니다. 복원 후 Windows/DHCP 클라이언트가 임대 정보 등을 다시 갱신하는 것까지 과거와 영구적으로 동일하다고 보장하지는 않습니다.
7. **종료**: 원상복구하지 않은 변경이 있을 때만 "복구하고 종료 / 변경 유지 후 종료 / 취소"를 묻습니다(선택은 저장되지 않음). 메모리 백업만 있는 상태로 변경을 유지하고 종료하면 다음 실행에서 전체 원상복구가 불가능할 수 있으므로, 유지하려면 먼저 **고급 > 복구 파일 내보내기**를 사용하세요.

### 고급 메뉴

- **복구 파일 내보내기/불러오기**: 사용자가 직접 선택할 때만 파일을 만듭니다. 버전 있는 텍스트 형식으로 값 이름·자료형·원시 데이터를 16진수로 손실 없이 저장하며, 불러올 때 PC(MachineGuid)·어댑터(GUID + 장치 인스턴스 ID + 드라이버 키)·스키마를 검증합니다. 파일에 레지스트리 경로는 들어가지 않으며, 파일 내용으로 임의 경로에 쓰는 기능이 아닙니다.
- **제조사 기본 MAC 사용 (NetworkAddress 제거)**: `NetworkAddress` 값만 제거하고 어댑터를 재시작합니다. TCP/IP 설정은 건드리지 않으며 "변경 전 상태 복원"과는 다른 기능입니다(삭제했던 TCP/IP 설정을 되살리지 않음). 영구 MAC 을 알 수 있을 때만 결과를 검증합니다.

### 취소와 동시 실행

작업은 UI 스레드 밖에서 실행되고 진행 중에는 버튼이 잠겨 중복 클릭·동시 변경이 불가능합니다. "취소"는 단계 경계에서 반영되며, 이미 어댑터를 중지했다면 보상 복구(레지스트리 되돌림 + 재활성화)를 수행합니다. 작업 중 창을 닫으면 바로 종료하지 않고 취소 요청 후 마무리되면 종료 절차를 계속합니다.

## 포터블 정책의 의미

"포터블"은 **앱 자체의 설치·설정 잔여물을 만들지 않음**을 뜻합니다. 사용자가 요청한 네트워크 설정 변경(레지스트리 `NetworkAddress`, TCP/IP 값 삭제)은 명시적인 작업 결과로 시스템에 남습니다. Windows 가 자체적으로 생성하는 실행·감사 기록(Prefetch, 이벤트 로그 등)까지 없다는 뜻이 아니며, 시스템 기록 삭제 기능은 구현하지 않습니다.

## 소스 구성

```
src/core/   플랫폼 독립 코어 — 추상 백엔드 위에서 동작, Linux 에서도 컴파일·테스트
  mac_address.*   MAC 파싱/서식/검증, 랜덤 생성 규칙
  registry_types.* 레지스트리 값 모델, 형식화된 키 식별자(두 종류의 키만 지정 가능)
  backend.h       IRegistryBackend / IAdapterControl / IRandomSource / ISystem
  backup.*        변경 전 상태 캡처(스냅샷 2회 일치 확인), 세션 메모리 저장소
  cleanup.*       EnableDHCP 제외 삭제 계획·실행·검증, 스냅샷 복원, 고정 IP/DNS 감지
  engine.*        변경/복구 절차와 단계별 보상 복구
  restore_file.*  복구 파일 직렬화·검증(버전 있음)
  describe_ko.*   결과의 한국어 설명
src/win/    Windows 백엔드 — win_registry(RegEnumValueW 등), win_adapters(IP Helper+SetupAPI 식별, NDIS 영구 MAC), win_device(DICS_DISABLE/ENABLE), win_random(BCrypt), win_system
src/gui/    Win32 한국어 GUI(main.cpp), 매니페스트, 리소스
tests/unit/        가짜 백엔드 단위 테스트(50개) — 실제 레지스트리/장치를 건드리지 않음
tests/integration/ Windows 전용 HKCU 격리 키 통합 테스트
docs/       BUILD.md(도구 집합·명령), COMPAT.md(지원 범위), TESTING.md(검증 결과·체크리스트)
dist/       미리 빌드한 EXE 와 SHA256
```

## 참고 문서

구현 기준으로 삼은 Microsoft 문서(작업 환경에서는 네트워크 정책으로 직접 열 수 없어 알려진 내용을 기준으로 적용함 — [docs/TESTING.md](docs/TESTING.md) 참고):

- [NdisReadNetworkAddress — NetworkAddress 처리](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ndis/nf-ndis-ndisreadnetworkaddress)
- [HLK: MAC 주소 변경·검증·복원 테스트(AddressChange)](https://learn.microsoft.com/en-us/windows-hardware/test/hlk/testref/d423a0dd-926e-491c-a437-0d7bc8ead98c)
- [RegEnumValueW](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regenumvaluew)
- [Visual C++ 지원 플랫폼](https://learn.microsoft.com/en-us/cpp/overview/supported-platforms-visual-cpp)

## 라이선스

MIT
