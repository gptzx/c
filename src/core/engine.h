// engine.h - MAC 변경/원상복구 절차 엔진 (플랫폼 독립)
//
// 한 번의 "변경"은 다음 단계를 순서대로 실행한다.
//   검증 → 백업 → 어댑터 중지 → NetworkAddress 기록 → TCP/IP 값 삭제·검증 → 어댑터 재활성화 → 실제 MAC·연결 상태 확인
// 운영체제가 보장하는 원자적 트랜잭션이 아니다. 단계별로 실행하며, 실패하면 보상 복구로 직전 상태로 되돌린다.
#pragma once
#include <atomic>
#include <functional>
#include <optional>
#include <string>
#include <vector>
#include "backend.h"
#include "backup.h"
#include "cleanup.h"

namespace macchg {

enum class Phase { Validating, BackingUp, Disabling, Applying, CleaningTcpip, Enabling, Verifying, RollingBack, Done };

enum class Step { None, Validate, Backup, Disable, WriteNetworkAddress, CleanupTcpip, Enable, Verify, Rollback, RestoreRegistry };

enum class Outcome {
    // 변경(Change)
    Success,                      // 실제 MAC 확인 완료. 정리도 검증됨
    RebootRequired,               // 레지스트리 기록 완료. 장치가 재부팅 필요를 보고해 실제 적용은 재부팅 후 확인 필요
    AppliedAdapterDisabled,       // 원래 비활성 어댑터: 레지스트리 기록 완료, 비활성 유지, 실제 MAC 미확인
    AppliedUnverified,            // 레지스트리 기록 완료, 기대 MAC 을 알 수 없어 검증 불가(제조사 기본 MAC 모드 등)
    VerifyTimeout,                // 재활성화 후 어댑터/MAC 확인 시간 초과. 변경 사항은 유지됨
    DriverIgnoredRolledBack,      // 드라이버가 값을 무시 → 직전 상태로 복구 완료
    DriverIgnoredRollbackFailed,  // 드라이버가 값을 무시 + 복구 실패
    FailedRolledBack,             // 단계 실패 → 직전 상태로 복구 완료
    FailedRollbackFailed,         // 단계 실패 + 복구 실패 (남은 변경 사항 있음)
    CancelledRolledBack,          // 사용자 취소 → 복구 완료
    CancelledRollbackFailed,
    NothingChanged,               // 검증/백업/중지 단계 실패: 아무것도 변경되지 않음
    // 복구(Restore)
    Restored,                     // 레지스트리 복원 + 실제 MAC 이 기준과 일치
    RestoredUnverified,           // 레지스트리 복원 완료, MAC 검증 불가(기준 MAC 미상/비활성/시간 초과)
    RestoredMacMismatch,          // 레지스트리 복원 완료, 실제 MAC 이 기준과 다름(재부팅 필요 가능)
};

// 복원이 의미상 완료된 결과인지(세션 기준을 지울 수 있는지)
inline bool IsRegistryRestored(Outcome o) { return o == Outcome::Restored || o == Outcome::RestoredUnverified || o == Outcome::RestoredMacMismatch; }
// 상태가 작업 전과 동일한 결과인지(최초 변경이었다면 기준을 지워도 됨)
inline bool IsFullyRolledBack(Outcome o) {
    return o == Outcome::NothingChanged || o == Outcome::FailedRolledBack || o == Outcome::CancelledRolledBack || o == Outcome::DriverIgnoredRolledBack;
}

struct Report {
    Outcome outcome = Outcome::NothingChanged;
    Step failedStep = Step::None;
    Status error;                           // 1차 실패 원인
    Status rollbackError;                   // 복구 중 실패 원인
    std::optional<Mac> requestedMac;        // 요청한 MAC (SetMac 모드)
    std::optional<Mac> previousMac;         // 작업 전 현재 MAC
    std::optional<Mac> observedMac;         // 작업 후 관찰된 MAC
    bool operUp = false;                    // 작업 후 연결 상태(MAC 적용 성공과 별개)
    bool rebootRequired = false;
    std::vector<std::wstring> deletedValues;   // 삭제한 TCP/IP 값 이름
    bool enableDhcpPreserved = false;          // EnableDHCP 가 존재해 보존 대상이었는지
    bool enableDhcpIntact = true;              // 정리 직후 검증에서 EnableDHCP 가 그대로였는지
    std::vector<std::wstring> remainingChanges; // 복구 실패 시 남은 변경 사항 설명
    bool baselineRecorded = false;             // 이 작업으로 세션 기준(최초 변경 전 상태)이 새로 기록됨
    bool baselineCleared = false;              // 작업 결과로 세션 기준이 제거됨(복원 완료 또는 최초 작업 완전 복구)
    StaticSettingsSummary staticSettings;      // 삭제 대상 중 고정 IP/DNS 요약
};

struct ChangeRequest {
    enum class Mode { SetMac, RemoveNetworkAddress };
    AdapterIdentity identity;
    std::wstring displayName;
    Mode mode = Mode::SetMac;
    Mac newMac;                               // SetMac 모드
    std::optional<Mac> expectedMacAfterRemove; // RemoveNetworkAddress 모드의 기대값(영구 MAC). 모르면 nullopt
    bool cleanupTcpip = true;                  // 기본 구성: 변경과 함께 TCP/IP 값 정리
    uint32_t verifyTimeoutMs = 20000;
    uint32_t pollIntervalMs = 500;
    uint32_t settleMs = 3000;                  // 불일치 판정 전 안정화 대기
    std::atomic<bool>* cancel = nullptr;       // 단계 경계에서만 확인
};

struct RestoreRequest {
    AdapterIdentity identity;
    AdapterBackup baseline;                   // 돌아갈 상태(세션 기준 또는 복구 파일)
    bool clearSessionBaselineOnSuccess = true;
    uint32_t verifyTimeoutMs = 20000;
    uint32_t pollIntervalMs = 500;
    uint32_t settleMs = 3000;
    std::atomic<bool>* cancel = nullptr;
};

using ProgressFn = std::function<void(Phase, const std::wstring&)>;

class Engine {
public:
    Engine(IRegistryBackend& reg, IAdapterControl& dev, ISystem& sys, SessionBackupStore& store);

    Report Change(const ChangeRequest& req, const ProgressFn& progress);
    Report Restore(const RestoreRequest& req, const ProgressFn& progress);

private:
    struct VerifyResult {
        bool observed = false;   // MAC 을 한 번이라도 관찰했는지
        bool match = false;      // 기대값과 일치
        bool timedOut = false;
        AdapterState last;
    };
    VerifyResult WaitForMac(const AdapterIdentity& id, const std::optional<Mac>& expected, uint32_t timeoutMs, uint32_t pollMs, uint32_t settleMs);

    // 레지스트리를 백업 상태로 되돌린다(NetworkAddress + TCP/IP 값). 장치는 건드리지 않는다.
    Status ApplyRegistryBackup(const AdapterBackup& b, std::vector<std::wstring>& remaining);

    void Emit(const ProgressFn& p, Phase ph, const std::wstring& msg) { if (p) p(ph, msg); }
    static bool Cancelled(std::atomic<bool>* c) { return c && c->load(); }

    IRegistryBackend& reg_;
    IAdapterControl& dev_;
    ISystem& sys_;
    SessionBackupStore& store_;
};

} // namespace macchg
