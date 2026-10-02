# 테스트와 검증 결과

## 요약

| 항목 | 결과 | 비고 |
|---|---|---|
| 코어 단위 테스트(가짜 백엔드) | **50/50 통과** (Linux x86-64, GCC 13.3) | `./build/host-tests/macchg_unit_tests` |
| Windows x64/x86 교차 빌드 | **성공** (GUI, 단위 테스트 EXE, 통합 테스트 EXE) | MinGW-w64 GCC 13.2, 경고 0 |
| Windows 단위 테스트 EXE 실행 | 미실행 | 이 환경에 Windows/Wine 없음 |
| HKCU 격리 키 통합 테스트 | 빌드만 성공, 미실행 | 동일 |
| GUI 실행·수동 검증 | 미실행 | 동일 |
| 실제 유선랜 MAC 변경/복구 | **미검증** | 하드웨어·Windows 필요 |
| 실제 무선랜 MAC 변경/복구 | **미검증** | 하드웨어·Windows 필요 |
| Windows 7/8/8.1/10/11 실행 | **미검증** | 하드웨어·Windows 필요 |
| MSVC 빌드 | 미실행 | 구성만 제공 |

작업 환경의 네트워크 정책으로 `learn.microsoft.com` 접근이 차단되어 요구사항에 명시된 4개 문서를 직접 열지 못했습니다. HLK "AddressChange" 테스트 절차(현재 NetworkAddress 읽기 → 현재/영구 주소 조회 → 드라이버 중지 → 레지스트리 기록 → 드라이버 시작 → 현재 주소 비교 → 영구 주소 불변 확인)는 검색 결과 요약으로 확인했으며, 이 프로그램의 변경 절차는 같은 순서를 따릅니다. `NdisReadNetworkAddress`(NetworkAddress 문자열 형식, 드라이버의 적용 재량), `RegEnumValueW`(열거 중 키 변경 금지, 이름 최대 16383자, ERROR_MORE_DATA 처리), "Visual C++ 지원 플랫폼"(VS 2022 → Windows 7 SP1/8.1/10/11) 은 기존 지식에 근거했습니다. 배포 전 원문을 다시 확인하기를 권장합니다.

## 단위 테스트 목록 (tests/unit)

실행 결과(발췌):

```
[PASS] RandomMac_LengthCharsAndBits
[PASS] RandomMac_FixFirstByte02
[PASS] RandomMac_AvoidsCollisionsWithOtherAdapters
[PASS] RandomMac_RngFailureIsErrorNotFallback
[PASS] Plan_ExcludesEnableDhcpCaseInsensitive_IncludesDefaultValue
[PASS] Execute_DeletesAllButEnableDhcp_PreservesTypeAndData_LeavesSubkeysAndOthers
[PASS] Execute_DoesNotCreateEnableDhcpWhenAbsent
[PASS] Execute_PartialFailureIsNotSuccess
[PASS] RestoreSnapshot_RestoresAllTypesLongValuesAndDefault_RemovesExtras
[PASS] Change_HappyPath_WritesRegSz_CleansTcpip_VerifiesMac_RecordsBaseline
[PASS] Change_Repeated_KeepsFirstBaseline_RestoreReturnsToInitial
[PASS] Change_DriverIgnoresValue_NoFalseSuccess_RolledBack
[PASS] Change_RebootRequiredFlag_IsReportedNotSuccess
[PASS] Change_CleanupPartialFailure_RestoresDeletedValues_AndNetworkAddress
[PASS] Change_EnableFailsPersistently_RollbackFailed_ReportsRemaining
[PASS] Change_CancelBeforeWrite_RollsBack_ReenablesAdapter
[PASS] Restore_RemovesValuesRecreatedByDhcp_RestoresExactInitialState
[PASS] Restore_WriteFailure_RollsBackToPreRestoreState_KeepsBaseline
[PASS] RestoreFile_RoundTripPreservesEverything
[PASS] RestoreFile_ValidateMachineAndAdapter
50 tests run, 0 check(s) failed
```

요구사항 대응:

| 요구 항목 | 테스트 |
|---|---|
| 랜덤 MAC 길이·문자 범위·유니캐스트·로컬 관리 비트, 두 번째 문자 2/6/A/E | `RandomMac_LengthCharsAndBits`, `RandomMac_FixFirstByte02` |
| 중복 회피, 난수 실패 시 오류(대체 없음) | `RandomMac_AvoidsCollisionsWithOtherAdapters`, `RandomMac_CollisionExhaustionFails`, `RandomMac_RngFailureIsErrorNotFallback` |
| EnableDHCP 대소문자, 부재, 자료형·데이터 보존, 0 유지 | `Plan_ExcludesEnableDhcpCaseInsensitive_IncludesDefaultValue`, `Plan_NoEnableDhcp`, `Execute_DoesNotCreateEnableDhcpWhenAbsent`, `Change_EnableDhcpZero_IsKeptZero_AndStaticSettingsReported` |
| 이름 없는 기본값, 여러 자료형, 긴 값(5000바이트) 삭제·복원 | `Execute_DeletesAllButEnableDhcp_…`, `RestoreSnapshot_RestoresAllTypesLongValuesAndDefault_RemovesExtras` |
| 열거 후 삭제(동시 삭제 금지), 재생성 값 검증 | `Verify_DetectsValueRecreatedAfterPlan`, `Change_UnstableBackupSnapshot_Aborts` |
| 일부 삭제 실패 = 전체 실패 아님 | `Execute_PartialFailureIsNotSuccess`, `Change_CleanupPartialFailure_RestoresDeletedValues_AndNetworkAddress` |
| 반복 변경 후 최초 상태 복원 | `Change_Repeated_KeepsFirstBaseline_RestoreReturnsToInitial`, `SessionStore_DoesNotOverwriteOriginal` |
| 단계별 실패 시 직전 상태 복구 | `Change_DisableFails_NothingChanged`, `Change_WriteNetworkAddressFails_RollsBack`, `Change_EnableFailsOnce_RollbackReenables`, `Change_EnableFailsPersistently_RollbackFailed_ReportsRemaining`, `Change_DeviceVanishesAfterDisable_ReportsNotFound`, `Restore_WriteFailure_RollsBackToPreRestoreState_KeepsBaseline` |
| 다른 어댑터·하위 키 불변 | `Execute_DeletesAllButEnableDhcp_…`, `Change_HappyPath_…`, `Restore_TwoAdaptersIndependent` |
| 드라이버가 값을 무시해도 성공 오판 없음 | `Change_DriverIgnoresValue_NoFalseSuccess_RolledBack`, `Change_DriverIgnoresAfterEarlierSuccess_…`, `Change_RebootRequiredFlag_IsReportedNotSuccess`, `Change_VerifyTimeout_NotSuccess_ChangesKept` |
| 비활성 어댑터 상태 보존 | `Change_OriginallyDisabledAdapter_StaysDisabled_NotReportedAsSuccess`, `Restore_BaselineDisabledButCurrentlyEnabled_EndsDisabled` |
| 식별 불일치 시 쓰기 시작 안 함 | `Change_IdentityMismatch_NoWritesNoDeviceCalls`, `Restore_IdentityMismatch_NothingChanged` |
| 취소 시 어댑터 방치 없음 | `Change_CancelBeforeWrite_RollsBack_ReenablesAdapter` |
| 복구 파일 왕복·손상 거부·PC/어댑터 검증 | `RestoreFile_*` 4개 |
| 제조사 기본 MAC(고급) 은 TCP/IP 를 건드리지 않음 | `Change_RemoveNetworkAddressMode_FactoryMac_NoTcpipCleanup` |

## Windows 에서 실행해야 할 검증

### 자동

```bat
build\msvc-x64\Release\macchg_unit_tests.exe
build\msvc-x64\Release\macchg_integration_tests.exe
```

(MinGW 빌드는 `dist/` 또는 `build/mingw-*/` 의 같은 이름 EXE.) 통합 테스트는 HKCU 격리 키만 사용하므로 관리자 권한 없이 실행할 수 있습니다.

### 수동(하드웨어) — 체크리스트

관리자 권한으로 `MacChanger.exe` 실행 후 어댑터마다 기록합니다. 결과를 아래 표에 채워 커밋하세요.

1. 어댑터 목록에 유선/무선이 올바른 종류·이름·연결 상태·현재 MAC 으로 표시되는지. 상세 정보에서 GUID·장치 인스턴스 ID·드라이버 키·영구 MAC(또는 "확인 불가") 확인.
2. `regedit` 로 `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` 의 값 목록을 미리 캡처(내보내기).
3. "랜덤 생성" → "변경". 상태 표시가 백업 중 → 적용 중 → 확인 중 → 완료 로 진행되는지, 네트워크가 잠시 끊긴 뒤 복귀하는지.
4. 결과: "변경 완료" 인 경우 `ipconfig /all` 의 물리적 주소가 요청값과 같은지. 레지스트리 `####\NetworkAddress` 가 구분자 없는 12자리 REG_SZ 인지. Tcpip 인터페이스 키에 `EnableDHCP` 만 남았는지(이후 DHCP 클라이언트가 값을 다시 만드는 것은 정상).
5. 무선랜에서 "적용 실패(지원하지 않음)" 이면 "첫 바이트 02 고정" 으로 재시도. Windows 설정의 "임의 하드웨어 주소" 가 켜져 있으면 끈 뒤(사용자가 직접) 재시도해 보고 결과 기록.
6. 랜덤 변경을 2회 이상 반복한 뒤 "원상복구" → 2번에서 캡처한 값 목록과 `NetworkAddress` 부재/존재가 동일한지, MAC 이 원래 값인지.
7. 변경한 상태로 "고급 > 복구 파일 내보내기" → 프로그램 종료("변경 유지 후 종료") → 재실행 → "복구 파일 불러오기" → 원상복구 → 2번과 비교.
8. 처음부터 비활성인 어댑터에서 변경 → "기록 완료(미확인)" 으로 표시되고 어댑터가 비활성으로 남는지.
9. 변경 중 USB 어댑터 분리(가능하면) → 실패 단계·오류 코드·남은 변경 사항 표시 확인.
10. 32비트 EXE 를 64비트 Windows 에서 실행 → 조회만 가능하고 x64 안내가 표시되는지.
11. 실행 전후로 EXE 옆·`%TEMP%`·`%APPDATA%`·`HKCU\Software` 에 프로그램 파일/키가 생기지 않았는지(Process Monitor 로 확인 가능).

### 검증 기록

| OS/빌드 | 어댑터(유선/무선, 모델, 드라이버 버전) | 변경 | 02 고정 필요 여부 | 원상복구 | 복구 파일 | 비고 |
|---|---|---|---|---|---|---|
| (아직 없음) | | | | | | |
