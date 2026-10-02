// win_adapters.h - 어댑터 열거와 정확한 식별 (IP Helper + SetupAPI + 드라이버 레지스트리 키)
//
// 식별 원칙: IP Helper 의 AdapterName(인터페이스 GUID) 과 SetupAPI 네트워크 클래스 장치의
// 드라이버 키(DIREG_DRV)에 있는 NetCfgInstanceId 를 대응시킨다. 그 장치의 SPDRP_DRIVER 속성에서
// Control\Class\{4D36E972-...}\#### 의 #### 를 얻고, SetupDiGetDeviceInstanceId 로 장치 인스턴스 ID 를 얻는다.
// 셋이 1:1 로 확정되지 않으면 쓰기 작업 대상이 될 수 없다.
#pragma once
#include <windows.h>
#include <setupapi.h>
#include <optional>
#include <string>
#include <vector>
#include "core/backend.h"

namespace macchg {

// NCF_* (netcfgx.h / ndisguid 관련 문서화된 어댑터 특성 플래그)
enum : uint32_t { NCF_VIRTUAL = 0x1, NCF_SOFTWARE_ENUMERATED = 0x2, NCF_PHYSICAL = 0x4, NCF_HIDDEN = 0x8, NCF_NO_SERVICE = 0x10, NCF_NOT_USER_REMOVABLE = 0x20, NCF_HAS_UI = 0x80, NCF_FILTER = 0x400 };

struct NetDeviceRecord {
    std::wstring instanceId;
    std::wstring driverKeyIndex;     // "0001"
    std::wstring netCfgInstanceId;   // "{GUID}" (대문자)
    std::wstring enumerator;         // PCI/USB/ROOT/SWD/BTH...
    std::wstring driverDesc, driverVersion, driverDate, providerName, manufacturer;
    uint32_t characteristics = 0;
    bool hasCharacteristics = false;
    bool enabled = true;             // CM_PROB_DISABLED 아님
    bool hasProblem = false;
    uint32_t problemCode = 0;
    bool rebootRequired = false;     // DI_NEEDREBOOT | DI_NEEDRESTART
};

struct AdapterInfo {
    AdapterIdentity id;
    std::wstring friendlyName;       // IP Helper FriendlyName (예: "이더넷", "Wi-Fi")
    std::wstring description;        // IP Helper Description 또는 DriverDesc
    enum class Kind { Ethernet, Wireless, Bluetooth, Loopback, Tunnel, Ppp, Other } kind = Kind::Other;
    bool isPhysical = false;         // NCF_PHYSICAL && !NCF_VIRTUAL
    bool deviceResolved = false;     // SetupAPI 장치와 1:1 대응
    bool ambiguous = false;          // 같은 GUID 장치가 둘 이상
    bool enabled = true;
    bool interfaceVisible = false;   // IP Helper 에 보임(비활성 장치는 보이지 않음)
    std::optional<Mac> currentMac;
    std::optional<Mac> permanentMac; // NDIS OID_802_3_PERMANENT_ADDRESS, 실패 시 nullopt("확인 불가")
    std::optional<Mac> ndisCurrentMac; // OID_802_3_CURRENT_ADDRESS (교차 확인용)
    uint32_t ifType = 0;
    uint32_t operStatus = 0;         // IF_OPER_STATUS (1=Up, 2=Down, ...)
    NetDeviceRecord device;
    std::optional<RegValue> networkAddressValue;  // 현재 Control\Class\...\####\NetworkAddress (상세 표시용)
    std::optional<bool> osRandomMacEnabled;       // Wi-Fi 임의 하드웨어 주소(WlanSvc 레지스트리 기반 추정)
};

// SetupAPI 로 네트워크 클래스(GUID_DEVCLASS_NET) 장치를 모두 열거한다(present 만).
Status EnumerateNetDevices(std::vector<NetDeviceRecord>& out);
// IP Helper + SetupAPI 결합 목록. 가상/터널/루프백도 포함하며 분류 정보를 담는다. 필터링은 UI 가 한다.
Status EnumerateAdapters(std::vector<AdapterInfo>& out);
// 복구 파일 검증용: 현재 확정 가능한(1:1) 어댑터 식별 정보 목록
Status ResolveLiveIdentities(std::vector<AdapterIdentity>& out);

std::wstring KindLabel(AdapterInfo::Kind k);
std::wstring OperStatusLabel(uint32_t operStatus, bool enabled, bool interfaceVisible);
std::wstring IfTypeLabel(uint32_t ifType);
// 상세 정보 텍스트(한국어)
std::wstring FormatAdapterDetails(const AdapterInfo& a);

// \\.\{GUID} 에 IOCTL_NDIS_QUERY_GLOBAL_STATS 로 OID 를 질의한다. 실패 시 false.
bool QueryNdisMac(const std::wstring& guid, uint32_t oid, Mac& out, uint32_t* err);

// 장치 인스턴스 ID 로 장치 정보 집합과 SP_DEVINFO_DATA 를 연다. 성공 시 호출자가 SetupDiDestroyDeviceInfoList 를 호출해야 한다.
Status OpenNetDeviceByInstanceId(const std::wstring& instanceId, HDEVINFO& devs, SP_DEVINFO_DATA& did);
// 열린 장치에서 레코드를 채운다.
Status FillNetDeviceRecord(HDEVINFO devs, SP_DEVINFO_DATA& did, NetDeviceRecord& out);

} // namespace macchg
