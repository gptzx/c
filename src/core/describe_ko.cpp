#include "describe_ko.h"

namespace macchg {

std::wstring DescribeOutcome(Outcome o) {
    switch (o) {
    case Outcome::Success: return L"변경 완료: 실제 MAC 이 요청한 값과 일치합니다.";
    case Outcome::RebootRequired: return L"재부팅 필요: 레지스트리에는 기록했지만 장치가 재부팅 필요를 보고해 실제 적용은 재부팅 후 확인해야 합니다.";
    case Outcome::AppliedAdapterDisabled: return L"기록 완료(미확인): 어댑터가 원래 비활성 상태여서 그대로 두었습니다. 실제 MAC 은 어댑터를 사용하도록 설정한 뒤 확인하세요.";
    case Outcome::AppliedUnverified: return L"기록 완료(검증 불가): 기대 MAC 을 알 수 없어 실제 적용 여부를 판정하지 못했습니다.";
    case Outcome::VerifyTimeout: return L"확인 불가(시간 초과): 재활성화 후 어댑터/MAC 을 제한 시간 안에 확인하지 못했습니다. 변경 사항은 유지되어 있습니다.";
    case Outcome::DriverIgnoredRolledBack: return L"적용 실패(지원하지 않음): 드라이버가 NetworkAddress 값을 무시했습니다. 변경 전 상태로 되돌렸습니다.";
    case Outcome::DriverIgnoredRollbackFailed: return L"적용 실패(지원하지 않음) + 복구 실패: 드라이버가 값을 무시했고 되돌리기도 실패했습니다.";
    case Outcome::FailedRolledBack: return L"실패: 단계 실패 후 직전 상태로 복구했습니다.";
    case Outcome::FailedRollbackFailed: return L"실패 + 복구 실패: 남은 변경 사항이 있습니다.";
    case Outcome::CancelledRolledBack: return L"취소됨: 직전 상태로 복구했습니다.";
    case Outcome::CancelledRollbackFailed: return L"취소됨 + 복구 실패: 남은 변경 사항이 있습니다.";
    case Outcome::NothingChanged: return L"중단: 아무것도 변경되지 않았습니다.";
    case Outcome::Restored: return L"원상복구 완료: 레지스트리를 복원했고 실제 MAC 이 기준과 일치합니다.";
    case Outcome::RestoredUnverified: return L"원상복구 완료(MAC 미확인): 레지스트리를 복원했습니다. 실제 MAC 은 확인할 수 없었습니다.";
    case Outcome::RestoredMacMismatch: return L"원상복구(레지스트리 완료, MAC 불일치): 레지스트리는 복원했지만 실제 MAC 이 기준과 다릅니다. 재부팅이 필요할 수 있습니다.";
    }
    return L"알 수 없는 결과";
}

std::wstring DescribeStep(Step s) {
    switch (s) {
    case Step::None: return L"-";
    case Step::Validate: return L"검증";
    case Step::Backup: return L"백업";
    case Step::Disable: return L"어댑터 중지";
    case Step::WriteNetworkAddress: return L"NetworkAddress 기록";
    case Step::CleanupTcpip: return L"TCP/IP 값 삭제·검증";
    case Step::Enable: return L"어댑터 재활성화";
    case Step::Verify: return L"실제 MAC 확인";
    case Step::Rollback: return L"복구";
    case Step::RestoreRegistry: return L"레지스트리 복원";
    }
    return L"?";
}

std::wstring DescribePhase(Phase p) {
    switch (p) {
    case Phase::Validating: return L"검증 중";
    case Phase::BackingUp: return L"백업 중";
    case Phase::Disabling: return L"적용 중 (어댑터 중지)";
    case Phase::Applying: return L"적용 중";
    case Phase::CleaningTcpip: return L"적용 중 (TCP/IP 정리)";
    case Phase::Enabling: return L"적용 중 (어댑터 재활성화)";
    case Phase::Verifying: return L"확인 중";
    case Phase::RollingBack: return L"복구 중";
    case Phase::Done: return L"완료";
    }
    return L"";
}

std::wstring DescribeAppError(uint32_t code) {
    switch (code) {
    case APP_E_INVALID_MAC: return L"MAC 주소 형식이 잘못되었거나 허용되지 않는 주소입니다(멀티캐스트/00:00…/FF:FF…).";
    case APP_E_RNG_FAILED: return L"운영체제 암호학적 난수 생성기 호출이 실패했습니다. 대체 생성은 하지 않습니다.";
    case APP_E_MAC_COLLISION: return L"다른 어댑터와 겹치지 않는 MAC 을 생성하지 못했습니다.";
    case APP_E_IDENTITY_MISMATCH: return L"인터페이스 GUID·장치 인스턴스·드라이버 키의 대응을 확정할 수 없습니다. 쓰기 작업을 시작하지 않았습니다.";
    case APP_E_ADAPTER_NOT_FOUND: return L"어댑터(장치)를 찾을 수 없습니다. 분리되었거나 제거되었을 수 있습니다.";
    case APP_E_AMBIGUOUS_DEVICE: return L"같은 GUID 를 가진 장치가 둘 이상 있습니다.";
    case APP_E_CLEANUP_INCOMPLETE: return L"TCP/IP 값 삭제 후 검증에서 EnableDHCP 외 값이 남아 있습니다.";
    case APP_E_VERIFY_TIMEOUT: return L"제한 시간 안에 어댑터의 현재 MAC 을 확인할 수 없었습니다.";
    case APP_E_DRIVER_IGNORED: return L"드라이버가 NetworkAddress 값을 무시했습니다(현재 MAC 이 바뀌지 않음).";
    case APP_E_CANCELLED: return L"사용자가 취소했습니다.";
    case APP_E_BACKUP_INCOMPLETE: return L"백업 스냅샷을 안정적으로 확보하지 못했습니다(값이 계속 바뀜).";
    case APP_E_NO_ORIGINAL_BACKUP: return L"이 어댑터의 변경 전 상태(세션 기준)가 없습니다.";
    case APP_E_FILE_FORMAT: return L"복구 파일 형식이 잘못되었습니다.";
    case APP_E_FILE_MACHINE_MISMATCH: return L"복구 파일의 PC 식별 정보가 현재 PC 와 다릅니다.";
    case APP_E_FILE_ADAPTER_MISMATCH: return L"복구 파일의 어댑터 식별 정보가 현재 어댑터와 다릅니다.";
    case APP_E_BUSY: return L"다른 작업이 진행 중입니다.";
    case APP_E_RESTORE_MISMATCH: return L"복원 후 검증에서 값이 기준과 다릅니다.";
    case APP_E_UNSUPPORTED_WOW64: return L"32비트 빌드는 64비트 Windows 에서 어댑터를 제어할 수 없습니다(SetupAPI ERROR_IN_WOW64). x64 빌드를 사용하세요.";
    case APP_E_DEVICE_DISABLED_LEFT: return L"어댑터가 중지 상태로 남았습니다.";
    case APP_E_NOT_ELEVATED: return L"관리자 권한이 없습니다.";
    case APP_E_UNEXPECTED_STATE: return L"예상치 못한 상태입니다.";
    }
    return L"";
}

std::wstring DescribeError(const Status& s) {
    if (s.ok()) return L"-";
    std::wstring msg = DescribeAppError(s.code);
    std::wstring out;
    if (!msg.empty()) out = msg;
    else out = L"Windows 오류 코드 " + std::to_wstring(s.code);
    if (!s.where.empty()) out += L" [" + s.where + L"]";
    return out;
}

bool OutcomeLeavesChanges(Outcome o) {
    switch (o) {
    case Outcome::Success: case Outcome::RebootRequired: case Outcome::AppliedAdapterDisabled: case Outcome::AppliedUnverified: case Outcome::VerifyTimeout:
    case Outcome::DriverIgnoredRollbackFailed: case Outcome::FailedRollbackFailed: case Outcome::CancelledRollbackFailed:
        return true;
    default:
        return false;
    }
}

std::wstring FormatReport(const Report& r, std::wstring (*win32Message)(uint32_t)) {
    std::wstring s;
    s += L"결과: " + DescribeOutcome(r.outcome) + L"\r\n";
    if (r.requestedMac) s += L"요청 MAC: " + FormatPlain(*r.requestedMac) + L" (" + FormatColon(*r.requestedMac) + L")\r\n";
    if (r.previousMac) s += L"이전 MAC: " + FormatPlain(*r.previousMac) + L"\r\n";
    if (r.observedMac) s += L"확인된 현재 MAC: " + FormatPlain(*r.observedMac) + L"\r\n";
    else if (r.outcome != Outcome::NothingChanged) s += L"확인된 현재 MAC: 확인 불가\r\n";
    if (r.outcome == Outcome::Success || r.outcome == Outcome::Restored || r.outcome == Outcome::RestoredMacMismatch || r.outcome == Outcome::AppliedUnverified || r.outcome == Outcome::RebootRequired)
        s += std::wstring(L"연결 상태: ") + (r.operUp ? L"연결됨" : L"연결 안 됨 (MAC 적용 결과와는 별개이며, 잠시 후 '새로 고침'으로 다시 확인하세요)") + L"\r\n";
    if (r.rebootRequired) s += L"장치가 재부팅 필요 플래그를 보고했습니다.\r\n";
    if (!r.deletedValues.empty()) {
        s += L"삭제한 TCP/IP 값 (" + std::to_wstring(r.deletedValues.size()) + L"개): ";
        for (size_t i = 0; i < r.deletedValues.size(); ++i) { if (i) s += L", "; s += r.deletedValues[i].empty() ? L"(기본값)" : r.deletedValues[i]; }
        s += L"\r\n";
        s += r.enableDhcpPreserved ? (r.enableDhcpIntact ? L"EnableDHCP: 보존됨(자료형·데이터 동일)\r\n" : L"EnableDHCP: 보존 대상이었으나 정리 직후 값이 달라져 있음(다른 프로세스 변경 가능)\r\n")
                                   : L"EnableDHCP: 원래 없었으므로 만들지 않음\r\n";
    }
    if (r.staticSettings.HasAny()) {
        s += L"주의: 삭제 대상에 고정 설정이 포함되어 있었습니다.";
        if (r.staticSettings.dhcpDisabled) s += L" EnableDHCP=0(고정 IP 사용 중)이며 이 값은 변경하지 않았습니다.";
        s += L"\r\n";
        for (const auto& it : r.staticSettings.items) s += L"  - " + it + L"\r\n";
    }
    if (r.failedStep != Step::None) s += L"실패 단계: " + DescribeStep(r.failedStep) + L"\r\n";
    if (!r.error.ok()) {
        s += L"오류: " + DescribeError(r.error);
        if (win32Message && DescribeAppError(r.error.code).empty()) s += L" — " + win32Message(r.error.code);
        s += L"\r\n";
    }
    if (!r.rollbackError.ok()) {
        s += L"복구 오류: " + DescribeError(r.rollbackError);
        if (win32Message && DescribeAppError(r.rollbackError.code).empty()) s += L" — " + win32Message(r.rollbackError.code);
        s += L"\r\n";
    }
    if (!r.remainingChanges.empty()) {
        s += L"남은 변경 사항(수동 복구 필요):\r\n";
        for (const auto& c : r.remainingChanges) s += L"  - " + c + L"\r\n";
        s += L"복구 방법: 고급 > 복구 파일이 있으면 불러와 원상복구를 다시 시도하거나, 장치 관리자에서 어댑터를 '사용'으로 바꾼 뒤 '원상복구'를 다시 실행하세요.\r\n";
    }
    return s;
}

} // namespace macchg
