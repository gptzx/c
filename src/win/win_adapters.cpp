#include "win_adapters.h"
#include <winsock2.h>
#include <ws2tcpip.h>
#include <iphlpapi.h>
#include <cfgmgr32.h>
#include <devguid.h>
#include <winioctl.h>
#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <cwctype>
#include <map>
#include "core/mac_address.h"
#include "core/registry_types.h"
#include "win_registry.h"

// ntddndis.h 의 값들(헤더 가용성 차이를 피하기 위해 로컬 정의)
#ifndef IOCTL_NDIS_QUERY_GLOBAL_STATS
#define IOCTL_NDIS_QUERY_GLOBAL_STATS CTL_CODE(0x17 /*FILE_DEVICE_PHYSICAL_NETCARD*/, 0, METHOD_OUT_DIRECT, FILE_ANY_ACCESS)
#endif
#ifndef OID_802_3_PERMANENT_ADDRESS
#define OID_802_3_PERMANENT_ADDRESS 0x01010101
#endif
#ifndef OID_802_3_CURRENT_ADDRESS
#define OID_802_3_CURRENT_ADDRESS 0x01010102
#endif
#ifndef CM_PROB_DISABLED
#define CM_PROB_DISABLED 0x16
#endif
#ifndef CONFIGFLAG_DISABLED
#define CONFIGFLAG_DISABLED 0x00000001   // regstr.h
#endif

namespace macchg {

static std::wstring UpperW(const std::wstring& s) { std::wstring u = s; for (auto& c : u) c = static_cast<wchar_t>(std::towupper(static_cast<wint_t>(c))); return u; }

static std::wstring GetDevRegPropW(HDEVINFO devs, SP_DEVINFO_DATA& did, DWORD prop) {
    DWORD type = 0, cb = 0;
    SetupDiGetDeviceRegistryPropertyW(devs, &did, prop, &type, nullptr, 0, &cb);
    if (cb == 0) return L"";
    std::vector<wchar_t> buf(cb / sizeof(wchar_t) + 2, L'\0');
    if (!SetupDiGetDeviceRegistryPropertyW(devs, &did, prop, &type, reinterpret_cast<PBYTE>(buf.data()), cb, nullptr)) return L"";
    return std::wstring(buf.data());
}

static std::wstring ReadKeySz(HKEY h, const wchar_t* name) {
    DWORD type = 0, cb = 0;
    if (RegQueryValueExW(h, name, nullptr, &type, nullptr, &cb) != ERROR_SUCCESS) return L"";
    if (type != REG_SZ && type != REG_EXPAND_SZ) return L"";
    std::vector<wchar_t> buf(cb / sizeof(wchar_t) + 2, L'\0');
    if (RegQueryValueExW(h, name, nullptr, &type, reinterpret_cast<BYTE*>(buf.data()), &cb) != ERROR_SUCCESS) return L"";
    return std::wstring(buf.data());
}

static bool ReadKeyDword(HKEY h, const wchar_t* name, DWORD& v) {
    DWORD type = 0, cb = sizeof(v);
    if (RegQueryValueExW(h, name, nullptr, &type, reinterpret_cast<BYTE*>(&v), &cb) != ERROR_SUCCESS) return false;
    return type == REG_DWORD;
}

Status FillNetDeviceRecord(HDEVINFO devs, SP_DEVINFO_DATA& did, NetDeviceRecord& out) {
    NetDeviceRecord r;
    wchar_t inst[MAX_DEVICE_ID_LEN + 1] = {};
    DWORD need = 0;
    if (!SetupDiGetDeviceInstanceIdW(devs, &did, inst, MAX_DEVICE_ID_LEN, &need)) return Status::Fail(GetLastError(), L"SetupDiGetDeviceInstanceIdW");
    r.instanceId = inst;
    std::wstring driver = GetDevRegPropW(devs, did, SPDRP_DRIVER);   // "{4D36E972-...}\0001"
    size_t bs = driver.find_last_of(L'\\');
    if (bs != std::wstring::npos) r.driverKeyIndex = driver.substr(bs + 1);
    // 드라이버 키 접두가 네트워크 클래스인지 확인(그렇지 않으면 대응 불가)
    if (UpperW(driver).rfind(L"{4D36E972-E325-11CE-BFC1-08002BE10318}\\", 0) != 0) r.driverKeyIndex.clear();
    r.enumerator = GetDevRegPropW(devs, did, SPDRP_ENUMERATOR_NAME);
    r.manufacturer = GetDevRegPropW(devs, did, SPDRP_MFG);

    HKEY drv = SetupDiOpenDevRegKey(devs, &did, DICS_FLAG_GLOBAL, 0, DIREG_DRV, KEY_QUERY_VALUE | KEY_WOW64_64KEY);
    if (drv != INVALID_HANDLE_VALUE) {
        r.netCfgInstanceId = UpperW(ReadKeySz(drv, L"NetCfgInstanceId"));
        r.driverDesc = ReadKeySz(drv, L"DriverDesc");
        r.driverVersion = ReadKeySz(drv, L"DriverVersion");
        r.driverDate = ReadKeySz(drv, L"DriverDate");
        r.providerName = ReadKeySz(drv, L"ProviderName");
        DWORD ch = 0;
        if (ReadKeyDword(drv, L"Characteristics", ch)) { r.characteristics = ch; r.hasCharacteristics = true; }
        RegCloseKey(drv);
    }
    ULONG status = 0, problem = 0;
    if (CM_Get_DevNode_Status(&status, &problem, did.DevInst, 0) == CR_SUCCESS) {
        r.hasProblem = (status & DN_HAS_PROBLEM) != 0;
        r.problemCode = problem;
        r.enabled = !(r.hasProblem && problem == CM_PROB_DISABLED);
    } else {
        // 보조: CONFIGFLAG_DISABLED
        DWORD flags = 0, type = 0, cb = sizeof(flags);
        if (SetupDiGetDeviceRegistryPropertyW(devs, &did, SPDRP_CONFIGFLAGS, &type, reinterpret_cast<PBYTE>(&flags), cb, nullptr)) r.enabled = (flags & CONFIGFLAG_DISABLED) == 0;
    }
    SP_DEVINSTALL_PARAMS_W dip{};
    dip.cbSize = sizeof(dip);
    if (SetupDiGetDeviceInstallParamsW(devs, &did, &dip)) r.rebootRequired = (dip.Flags & (DI_NEEDREBOOT | DI_NEEDRESTART)) != 0;
    out = r;
    return Status::Ok();
}

Status EnumerateNetDevices(std::vector<NetDeviceRecord>& out) {
    out.clear();
    HDEVINFO devs = SetupDiGetClassDevsW(&GUID_DEVCLASS_NET, nullptr, nullptr, DIGCF_PRESENT);
    if (devs == INVALID_HANDLE_VALUE) return Status::Fail(GetLastError(), L"SetupDiGetClassDevsW");
    SP_DEVINFO_DATA did{};
    did.cbSize = sizeof(did);
    for (DWORD i = 0; SetupDiEnumDeviceInfo(devs, i, &did); ++i) {
        NetDeviceRecord r;
        if (FillNetDeviceRecord(devs, did, r).ok()) out.push_back(r);
    }
    DWORD err = GetLastError();
    SetupDiDestroyDeviceInfoList(devs);
    if (err != ERROR_NO_MORE_ITEMS && err != ERROR_SUCCESS) return Status::Fail(err, L"SetupDiEnumDeviceInfo");
    return Status::Ok();
}

Status OpenNetDeviceByInstanceId(const std::wstring& instanceId, HDEVINFO& devs, SP_DEVINFO_DATA& did) {
    devs = SetupDiCreateDeviceInfoList(&GUID_DEVCLASS_NET, nullptr);
    if (devs == INVALID_HANDLE_VALUE) return Status::Fail(GetLastError(), L"SetupDiCreateDeviceInfoList");
    did = SP_DEVINFO_DATA{};
    did.cbSize = sizeof(did);
    if (!SetupDiOpenDeviceInfoW(devs, instanceId.c_str(), nullptr, 0, &did)) {
        DWORD e = GetLastError();
        SetupDiDestroyDeviceInfoList(devs);
        devs = INVALID_HANDLE_VALUE;
        if (e == ERROR_NO_SUCH_DEVINST) return Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"SetupDiOpenDeviceInfoW");
        return Status::Fail(e, L"SetupDiOpenDeviceInfoW");
    }
    return Status::Ok();
}

bool QueryNdisMac(const std::wstring& guid, uint32_t oid, Mac& out, uint32_t* err) {
    std::wstring path = L"\\\\.\\" + guid;
    HANDLE h = CreateFileW(path.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) h = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) { if (err) *err = GetLastError(); return false; }
    DWORD code = oid;
    BYTE buf[16] = {};
    DWORD ret = 0;
    BOOL ok = DeviceIoControl(h, IOCTL_NDIS_QUERY_GLOBAL_STATS, &code, sizeof(code), buf, sizeof(buf), &ret, nullptr);
    DWORD e = ok ? 0 : GetLastError();
    CloseHandle(h);
    if (!ok || ret != 6) { if (err) *err = ok ? ERROR_INVALID_DATA : e; return false; }
    for (int i = 0; i < 6; ++i) out.b[i] = buf[i];
    return true;
}

static std::optional<bool> ReadWlanRandomMacState(const std::wstring& guid) {
    HKEY h = nullptr;
    std::wstring path = L"SOFTWARE\\Microsoft\\WlanSvc\\Interfaces\\" + guid;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path.c_str(), 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &h) != ERROR_SUCCESS) return std::nullopt;
    BYTE buf[16] = {};
    DWORD cb = sizeof(buf), type = 0;
    std::optional<bool> r;
    if (RegQueryValueExW(h, L"RandomMacState", nullptr, &type, buf, &cb) == ERROR_SUCCESS && cb >= 1) r = buf[0] != 0;
    RegCloseKey(h);
    return r;
}

struct IpAdapter {
    std::wstring guid;   // 대문자 "{...}"
    std::wstring friendlyName, description;
    uint32_t ifType = 0, operStatus = 0;
    std::optional<Mac> mac;
};

static Status EnumerateIpAdapters(std::vector<IpAdapter>& out) {
    out.clear();
    ULONG flags = GAA_FLAG_INCLUDE_ALL_INTERFACES | GAA_FLAG_SKIP_UNICAST | GAA_FLAG_SKIP_ANYCAST | GAA_FLAG_SKIP_MULTICAST | GAA_FLAG_SKIP_DNS_SERVER;
    ULONG size = 32 * 1024;
    std::vector<BYTE> buf;
    for (int attempt = 0; attempt < 5; ++attempt) {
        buf.resize(size);
        ULONG r = GetAdaptersAddresses(AF_UNSPEC, flags, nullptr, reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buf.data()), &size);
        if (r == ERROR_BUFFER_OVERFLOW) continue;
        if (r == ERROR_NO_DATA) return Status::Ok();
        if (r != NO_ERROR) return Status::Fail(r, L"GetAdaptersAddresses");
        for (PIP_ADAPTER_ADDRESSES a = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buf.data()); a; a = a->Next) {
            IpAdapter ia;
            std::string an = a->AdapterName ? a->AdapterName : "";
            ia.guid = UpperW(std::wstring(an.begin(), an.end()));
            ia.friendlyName = a->FriendlyName ? a->FriendlyName : L"";
            ia.description = a->Description ? a->Description : L"";
            ia.ifType = a->IfType;
            ia.operStatus = static_cast<uint32_t>(a->OperStatus);
            if (a->PhysicalAddressLength == 6) { Mac m; for (int i = 0; i < 6; ++i) m.b[i] = a->PhysicalAddress[i]; ia.mac = m; }
            out.push_back(ia);
        }
        return Status::Ok();
    }
    return Status::Fail(ERROR_BUFFER_OVERFLOW, L"GetAdaptersAddresses");
}

static AdapterInfo::Kind Classify(uint32_t ifType, const NetDeviceRecord& d) {
    if (ifType == IF_TYPE_IEEE80211) return AdapterInfo::Kind::Wireless;
    if (ifType == IF_TYPE_SOFTWARE_LOOPBACK) return AdapterInfo::Kind::Loopback;
    if (ifType == IF_TYPE_TUNNEL) return AdapterInfo::Kind::Tunnel;
    if (ifType == IF_TYPE_PPP) return AdapterInfo::Kind::Ppp;
    std::wstring en = UpperW(d.enumerator);
    if (en.rfind(L"BTH", 0) == 0) return AdapterInfo::Kind::Bluetooth;
    if (ifType == IF_TYPE_ETHERNET_CSMACD || ifType == 0) {
        // IP Helper 에 보이지 않는(비활성) 장치는 ifType 0: 드라이버 설명으로 무선 추정
        std::wstring desc = UpperW(d.driverDesc);
        if (ifType == 0 && (desc.find(L"WI-FI") != std::wstring::npos || desc.find(L"WIFI") != std::wstring::npos || desc.find(L"WIRELESS") != std::wstring::npos || desc.find(L"802.11") != std::wstring::npos || desc.find(L"WLAN") != std::wstring::npos))
            return AdapterInfo::Kind::Wireless;
        return AdapterInfo::Kind::Ethernet;
    }
    return AdapterInfo::Kind::Other;
}

Status EnumerateAdapters(std::vector<AdapterInfo>& out) {
    out.clear();
    std::vector<NetDeviceRecord> devs;
    Status s = EnumerateNetDevices(devs);
    if (!s.ok()) return s;
    std::vector<IpAdapter> ips;
    s = EnumerateIpAdapters(ips);
    if (!s.ok()) return s;

    // GUID 별 장치 수(중복 감지)
    std::map<std::wstring, int> guidCount;
    for (const auto& d : devs) if (!d.netCfgInstanceId.empty()) ++guidCount[d.netCfgInstanceId];

    WinRegistryBackend reg = WinRegistryBackend::ForSystem();
    std::map<std::wstring, bool> consumedIp;

    for (const auto& d : devs) {
        if (d.netCfgInstanceId.empty()) continue;   // 네트워크 인터페이스가 없는 장치(필터 등)
        AdapterInfo a;
        a.device = d;
        a.id.interfaceGuid = d.netCfgInstanceId;
        a.id.deviceInstanceId = d.instanceId;
        a.id.driverKeyIndex = d.driverKeyIndex;
        a.enabled = d.enabled;
        a.ambiguous = guidCount[d.netCfgInstanceId] > 1;
        a.deviceResolved = !a.ambiguous && !d.driverKeyIndex.empty();
        a.isPhysical = d.hasCharacteristics && (d.characteristics & NCF_PHYSICAL) && !(d.characteristics & NCF_VIRTUAL);
        a.description = d.driverDesc;
        const IpAdapter* ip = nullptr;
        for (const auto& i : ips) if (i.guid == d.netCfgInstanceId) { ip = &i; break; }
        if (ip) {
            consumedIp[ip->guid] = true;
            a.interfaceVisible = true;
            a.friendlyName = ip->friendlyName;
            if (!ip->description.empty()) a.description = ip->description;
            a.ifType = ip->ifType;
            a.operStatus = ip->operStatus;
            a.currentMac = ip->mac;
        }
        a.kind = Classify(a.ifType, d);
        if (a.deviceResolved) {
            // 드라이버 키의 NetCfgInstanceId 가 실제로 그 GUID 인지 Class 키에서 교차 확인
            bool found = false; RegValue v;
            if (reg.ReadValue(NetClassDriverKey(d.driverKeyIndex), L"NetCfgInstanceId", found, v).ok() && found && v.type == RT_SZ && UpperW(DecodeSz(v.data)) == d.netCfgInstanceId) {
                bool naFound = false; RegValue na;
                if (reg.ReadValue(NetClassDriverKey(d.driverKeyIndex), L"NetworkAddress", naFound, na).ok() && naFound) a.networkAddressValue = na;
            } else {
                a.deviceResolved = false;   // 대응 불일치: 쓰기 대상 불가
            }
        }
        if (a.enabled && a.interfaceVisible) {
            Mac m; uint32_t e = 0;
            if (QueryNdisMac(a.id.interfaceGuid, OID_802_3_PERMANENT_ADDRESS, m, &e)) a.permanentMac = m;
            if (QueryNdisMac(a.id.interfaceGuid, OID_802_3_CURRENT_ADDRESS, m, &e)) a.ndisCurrentMac = m;
        }
        if (a.kind == AdapterInfo::Kind::Wireless) a.osRandomMacEnabled = ReadWlanRandomMacState(a.id.interfaceGuid);
        out.push_back(a);
    }
    // SetupAPI 장치가 없는 IP Helper 인터페이스(루프백, 일부 터널): 식별 불가로 표시만 한다.
    for (const auto& i : ips) {
        if (consumedIp.count(i.guid)) continue;
        AdapterInfo a;
        a.id.interfaceGuid = i.guid;
        a.friendlyName = i.friendlyName;
        a.description = i.description;
        a.ifType = i.ifType;
        a.operStatus = i.operStatus;
        a.currentMac = i.mac;
        a.interfaceVisible = true;
        a.deviceResolved = false;
        a.kind = Classify(i.ifType, NetDeviceRecord{});
        out.push_back(a);
    }
    // 정렬: 물리 → 가상, 그 안에서 이름순
    std::stable_sort(out.begin(), out.end(), [](const AdapterInfo& x, const AdapterInfo& y) {
        if (x.isPhysical != y.isPhysical) return x.isPhysical;
        if (x.deviceResolved != y.deviceResolved) return x.deviceResolved;
        return x.friendlyName < y.friendlyName;
    });
    return Status::Ok();
}

Status ResolveLiveIdentities(std::vector<AdapterIdentity>& out) {
    out.clear();
    std::vector<AdapterInfo> all;
    Status s = EnumerateAdapters(all);
    if (!s.ok()) return s;
    for (const auto& a : all) if (a.deviceResolved) out.push_back(a.id);
    return Status::Ok();
}

std::wstring KindLabel(AdapterInfo::Kind k) {
    switch (k) {
    case AdapterInfo::Kind::Ethernet: return L"유선";
    case AdapterInfo::Kind::Wireless: return L"무선";
    case AdapterInfo::Kind::Bluetooth: return L"블루투스";
    case AdapterInfo::Kind::Loopback: return L"루프백";
    case AdapterInfo::Kind::Tunnel: return L"터널";
    case AdapterInfo::Kind::Ppp: return L"PPP/VPN";
    default: return L"기타";
    }
}

std::wstring OperStatusLabel(uint32_t operStatus, bool enabled, bool interfaceVisible) {
    if (!enabled) return L"비활성(장치 중지)";
    if (!interfaceVisible) return L"인터페이스 없음";
    switch (operStatus) {
    case IfOperStatusUp: return L"연결됨";
    case IfOperStatusDown: return L"연결 안 됨";
    case IfOperStatusTesting: return L"테스트 중";
    case IfOperStatusDormant: return L"대기(연결 시도 중)";
    case IfOperStatusNotPresent: return L"없음";
    case IfOperStatusLowerLayerDown: return L"하위 계층 다운";
    default: return L"알 수 없음";
    }
}

std::wstring IfTypeLabel(uint32_t t) {
    switch (t) {
    case IF_TYPE_ETHERNET_CSMACD: return L"6 (Ethernet)";
    case IF_TYPE_IEEE80211: return L"71 (IEEE 802.11)";
    case IF_TYPE_SOFTWARE_LOOPBACK: return L"24 (Loopback)";
    case IF_TYPE_TUNNEL: return L"131 (Tunnel)";
    case IF_TYPE_PPP: return L"23 (PPP)";
    case 0: return L"- (IP Helper 에 없음)";
    default: return std::to_wstring(t);
    }
}

std::wstring FormatAdapterDetails(const AdapterInfo& a) {
    std::wstring s;
    auto line = [&](const wchar_t* k, const std::wstring& v) { s += k; s += L": "; s += v.empty() ? L"-" : v; s += L"\r\n"; };
    line(L"이름", a.friendlyName);
    line(L"설명", a.description);
    line(L"종류", KindLabel(a.kind) + (a.isPhysical ? L" (물리)" : L" (가상/미확인)"));
    line(L"연결 상태", OperStatusLabel(a.operStatus, a.enabled, a.interfaceVisible));
    line(L"현재 MAC (IP Helper)", a.currentMac ? FormatPlain(*a.currentMac) + L"  " + FormatColon(*a.currentMac) : L"확인 불가");
    line(L"현재 MAC (NDIS OID)", a.ndisCurrentMac ? FormatPlain(*a.ndisCurrentMac) : L"확인 불가");
    line(L"영구 MAC (NDIS OID_802_3_PERMANENT_ADDRESS)", a.permanentMac ? FormatPlain(*a.permanentMac) + L"  " + FormatColon(*a.permanentMac) : L"확인 불가");
    line(L"인터페이스 GUID", a.id.interfaceGuid);
    line(L"장치 인스턴스 ID", a.id.deviceInstanceId);
    line(L"드라이버 키", a.id.driverKeyIndex.empty() ? L"-" : L"Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}\\" + a.id.driverKeyIndex);
    line(L"장치 식별 확정", a.deviceResolved ? L"예 (GUID ↔ 장치 인스턴스 ↔ 드라이버 키 1:1)" : (a.ambiguous ? L"아니오 (같은 GUID 장치 다수)" : L"아니오 (대응 불가)"));
    line(L"열거자", a.device.enumerator);
    line(L"제조사", a.device.manufacturer);
    line(L"드라이버 설명", a.device.driverDesc);
    line(L"드라이버 버전/날짜", a.device.driverVersion + L" / " + a.device.driverDate);
    line(L"드라이버 공급자", a.device.providerName);
    wchar_t ch[32]; swprintf(ch, 32, L"0x%08X", a.device.characteristics);
    line(L"Characteristics", a.device.hasCharacteristics ? std::wstring(ch) : L"-");
    line(L"IfType", IfTypeLabel(a.ifType));
    line(L"장치 상태", a.enabled ? L"사용" : L"사용 안 함");
    if (a.device.hasProblem) line(L"문제 코드", std::to_wstring(a.device.problemCode));
    if (a.device.rebootRequired) line(L"재부팅 필요 플래그", L"설정됨");
    line(L"현재 NetworkAddress 값", a.networkAddressValue ? RegTypeName(a.networkAddressValue->type) + L" " + SummarizeRegData(*a.networkAddressValue) : L"없음");
    if (a.kind == AdapterInfo::Kind::Wireless) {
        line(L"Windows Wi-Fi 임의 하드웨어 주소(WlanSvc 레지스트리 기반 추정)", a.osRandomMacEnabled ? (*a.osRandomMacEnabled ? L"켜짐으로 추정 — 이 설정은 이 프로그램이 바꾸지 않습니다. 켜져 있으면 결과에 영향을 줄 수 있습니다." : L"꺼짐으로 추정") : L"확인 불가");
        line(L"무선랜 지원 여부", L"미확인 — 실제 적용 결과로 판단합니다. 첫 바이트 02 고정 옵션이 도움이 될 수 있지만 모든 무선랜의 변경을 보장하지 않습니다.");
    }
    return s;
}

} // namespace macchg
