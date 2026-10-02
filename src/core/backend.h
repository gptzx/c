// backend.h - 코어가 의존하는 추상 백엔드 인터페이스.
// Windows 구현(src/win)과 테스트용 가짜 구현(tests/unit/fakes.h)이 이를 구현한다.
#pragma once
#include <cstdint>
#include <optional>
#include <string>
#include <vector>
#include "mac_address.h"
#include "registry_types.h"
#include "status.h"

namespace macchg {

// 레지스트리 접근. 키는 형식화된 RegKeyId 로만 지정된다(임의 경로 금지).
class IRegistryBackend {
public:
    virtual ~IRegistryBackend() = default;
    virtual Status KeyExists(const RegKeyId& key, bool& exists) = 0;
    // 키에 직접 속한 모든 값의 완전한 스냅샷(이름·자료형·원시 데이터).
    // 이름 없는 기본값은 name == L"" 로 포함된다(데이터가 있을 때만 열거됨).
    // 구현은 열거 도중 키를 변경하지 않아야 한다.
    virtual Status EnumerateValues(const RegKeyId& key, std::vector<RegValue>& out) = 0;
    // 값이 없으면 found=false 와 성공을 반환한다.
    virtual Status ReadValue(const RegKeyId& key, const std::wstring& name, bool& found, RegValue& out) = 0;
    virtual Status WriteValue(const RegKeyId& key, const RegValue& value) = 0;
    // 값이 없으면 existed=false 와 성공을 반환한다(멱등).
    virtual Status DeleteValue(const RegKeyId& key, const std::wstring& name, bool& existed) = 0;
};

// 어댑터 식별 정보: 세 가지가 모두 일치해야 같은 장치로 본다.
struct AdapterIdentity {
    std::wstring interfaceGuid;     // "{XXXXXXXX-....}" 대문자
    std::wstring deviceInstanceId;  // 예: "PCI\VEN_8086&DEV_15B8&SUBSYS_...\3&11583659&0&FE"
    std::wstring driverKeyIndex;    // 예: "0001" (Control\Class\{4D36E972-...}\0001)

    bool operator==(const AdapterIdentity& o) const {
        return NameEqualsNoCase(interfaceGuid, o.interfaceGuid) && NameEqualsNoCase(deviceInstanceId, o.deviceInstanceId) && driverKeyIndex == o.driverKeyIndex;
    }
    bool operator!=(const AdapterIdentity& o) const { return !(*this == o); }
};

struct AdapterState {
    bool present = false;            // PnP 장치가 존재
    bool enabled = false;            // 장치가 "사용" 상태(비활성 아님)
    bool rebootRequired = false;     // 장치 설치 매개변수에 재부팅 필요 플래그
    bool interfaceVisible = false;   // IP Helper 에 인터페이스가 보임
    std::optional<Mac> currentMac;   // IP Helper 가 보고한 현재 MAC
    bool operUp = false;             // 운영 상태 Up(연결됨)
};

class IAdapterControl {
public:
    virtual ~IAdapterControl() = default;
    // GUID ↔ 장치 인스턴스 ID ↔ 드라이버 키 인덱스 대응이 지금도 성립하는지 확인한다.
    // 성립하지 않으면 APP_E_IDENTITY_MISMATCH / APP_E_ADAPTER_NOT_FOUND / APP_E_AMBIGUOUS_DEVICE.
    virtual Status VerifyIdentity(const AdapterIdentity& id) = 0;
    virtual Status QueryState(const AdapterIdentity& id, AdapterState& out) = 0;
    // 선택한 장치만 중지/재활성화한다. 재부팅 필요 플래그를 보고한다.
    virtual Status SetEnabled(const AdapterIdentity& id, bool enable, bool& rebootRequired) = 0;
};

// 암호학적 난수. 실패하면 false (대체 생성 금지).
class IRandomSource {
public:
    virtual ~IRandomSource() = default;
    virtual bool Fill(uint8_t* buf, size_t len) = 0;
};

// 시간/대기. 테스트에서는 가짜 시계를 사용한다.
class ISystem {
public:
    virtual ~ISystem() = default;
    virtual uint64_t NowMs() = 0;
    virtual void SleepMs(uint32_t ms) = 0;
};

} // namespace macchg
