// backup.h - 변경 전 상태 백업 모델과 세션(메모리) 백업 저장소
#pragma once
#include <map>
#include <optional>
#include <string>
#include <vector>
#include "backend.h"

namespace macchg {

struct AdapterBackup {
    static constexpr uint32_t kSchemaVersion = 1;

    AdapterIdentity identity;
    std::wstring adapterName;                 // 표시용(검증에 사용하지 않음)
    bool adapterWasEnabled = true;            // 원래 활성화 상태
    std::optional<Mac> macAtBackup;           // 백업 시점에 관찰된 현재 MAC(모르면 nullopt)

    bool networkAddressExisted = false;       // 기존 NetworkAddress 존재 여부
    RegValue networkAddress;                  // 존재했을 때의 자료형/원시 데이터 (name == L"NetworkAddress")

    bool tcpipKeyExisted = true;              // Tcpip\Parameters\Interfaces\{GUID} 키 존재 여부
    std::vector<RegValue> tcpipValues;        // 그 키에 직접 속한 모든 값 (기본값 포함)
};

// 현재 상태를 백업으로 캡처한다. 열거 스냅샷을 두 번 찍어 일치할 때만 성공한다.
// 어떤 쓰기도 하지 않는다. 실패하면 APP_E_BACKUP_INCOMPLETE 또는 Win32 오류.
Status CaptureBackup(IRegistryBackend& reg, IAdapterControl& dev, const AdapterIdentity& id, const std::wstring& displayName, AdapterBackup& out);

// 세션 동안 메모리에만 보관되는 "최초 변경 전 상태" 저장소.
// 같은 어댑터를 여러 번 변경해도 최초 기준은 덮어쓰지 않는다.
class SessionBackupStore {
public:
    bool HasOriginal(const std::wstring& guid) const;
    const AdapterBackup* Original(const std::wstring& guid) const;
    // 이미 기준이 있으면 false 를 반환하고 덮어쓰지 않는다.
    bool SetOriginalIfAbsent(const AdapterBackup& b);
    // 복구 파일 불러오기처럼 사용자가 명시적으로 요청한 경우에만 사용한다.
    void ReplaceOriginal(const AdapterBackup& b);
    void ClearOriginal(const std::wstring& guid);
    std::vector<std::wstring> Guids() const;
    size_t Count() const { return originals_.size(); }

private:
    static std::wstring Key(const std::wstring& guid);
    std::map<std::wstring, AdapterBackup> originals_;
};

} // namespace macchg
