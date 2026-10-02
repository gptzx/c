// cleanup.h - TCP/IP 인터페이스 키 값 정리 계획/실행/검증 (플랫폼 독립)
//
// 규칙:
//  - 값 이름을 대소문자 구분 없이 비교하여 "EnableDHCP" 만 제외한다.
//  - EnableDHCP 의 원래 자료형과 원시 데이터는 그대로 둔다. 없었다면 만들지 않는다.
//  - 이름 없는 기본값(데이터가 있으면 열거됨)도 삭제 대상이다.
//  - 키 자체와 하위 키는 건드리지 않는다(백엔드 인터페이스가 하위 키를 가리킬 수 없다).
//  - 먼저 열거·백업한 스냅샷으로 계획을 세우고, 그 다음에 삭제한다.
//  - 일부 값을 삭제하지 못하면 전체 성공으로 처리하지 않는다.
#pragma once
#include <string>
#include <vector>
#include "backend.h"

namespace macchg {

extern const wchar_t* const kEnableDhcpName;  // L"EnableDHCP"

struct CleanupPlan {
    std::vector<RegValue> toDelete;   // 삭제 대상(스냅샷 기준)
    bool enableDhcpPresent = false;   // 보존 대상이 존재했는지
    RegValue enableDhcp;              // 존재했다면 원래 이름 표기·자료형·데이터
};

CleanupPlan PlanTcpipCleanup(const std::vector<RegValue>& snapshot);

// 고정 IP/DNS 등 삭제 시 사용자에게 알릴 만한 설정 요약.
struct StaticSettingsSummary {
    bool dhcpDisabled = false;               // EnableDHCP == 0 (고정 IP 사용 중일 가능성)
    std::vector<std::wstring> items;         // "IPAddress = \"192.168.0.10\"" 같은 표시용 항목
    bool HasAny() const { return dhcpDisabled || !items.empty(); }
};
StaticSettingsSummary DetectStaticSettings(const std::vector<RegValue>& snapshot);

// 계획에 따라 삭제하고 재열거로 검증한다. 남은 값(삭제 실패)은 remaining 에 담긴다.
// EnableDHCP 가 보존되었는지는 preservedIntact 로 보고한다(다른 프로세스 변경 가능성 때문에 실패로 보지 않음).
Status ExecuteTcpipCleanup(IRegistryBackend& reg, const RegKeyId& key, const CleanupPlan& plan, std::vector<std::wstring>& remaining, bool& preservedIntact);
Status VerifyTcpipCleanup(IRegistryBackend& reg, const RegKeyId& key, const CleanupPlan& plan, std::vector<std::wstring>& remaining, bool& preservedIntact);

// 키의 직접 값들을 스냅샷과 똑같이 만든다: 스냅샷에 없는 값은 삭제, 스냅샷 값은 모두 기록, 재열거로 검증.
// 하위 키는 건드리지 않는다. 복구(rollback/restore)에 사용한다.
Status RestoreValueSnapshot(IRegistryBackend& reg, const RegKeyId& key, const std::vector<RegValue>& snapshot, std::vector<std::wstring>& mismatches);

} // namespace macchg
