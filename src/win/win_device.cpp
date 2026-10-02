#include <windows.h>
#include <initguid.h>       // GUID_DEVCLASS_NET 정의(이 TU 에서만)
#include <devguid.h>
#include <setupapi.h>
#include <cfgmgr32.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <iphlpapi.h>
#include <cwctype>
#include <vector>
#include "win_device.h"
#include "win_adapters.h"

#ifndef CM_PROB_DISABLED
#define CM_PROB_DISABLED 0x16
#endif
#ifndef ERROR_IN_WOW64
#define ERROR_IN_WOW64 0xE0000235
#endif

namespace macchg {

static std::wstring UpperW(const std::wstring& s) { std::wstring u = s; for (auto& c : u) c = static_cast<wchar_t>(std::towupper(static_cast<wint_t>(c))); return u; }

// 장치를 열고 식별 정보(GUID·인스턴스·드라이버 키)가 모두 일치하는지 확인한다.
static Status OpenAndCheck(const AdapterIdentity& id, HDEVINFO& devs, SP_DEVINFO_DATA& did, NetDeviceRecord& rec) {
    Status s = OpenNetDeviceByInstanceId(id.deviceInstanceId, devs, did);
    if (!s.ok()) return s;
    s = FillNetDeviceRecord(devs, did, rec);
    if (!s.ok()) { SetupDiDestroyDeviceInfoList(devs); devs = INVALID_HANDLE_VALUE; return s; }
    if (rec.netCfgInstanceId != UpperW(id.interfaceGuid) || rec.driverKeyIndex != id.driverKeyIndex) {
        SetupDiDestroyDeviceInfoList(devs); devs = INVALID_HANDLE_VALUE;
        return Status::Fail(APP_E_IDENTITY_MISMATCH, L"OpenAndCheck");
    }
    return Status::Ok();
}

Status WinAdapterControl::VerifyIdentity(const AdapterIdentity& id) {
    // 1) 전체 열거에서 같은 GUID 장치가 정확히 하나인지
    std::vector<NetDeviceRecord> all;
    Status s = EnumerateNetDevices(all);
    if (!s.ok()) return s;
    int count = 0; const NetDeviceRecord* match = nullptr;
    for (const auto& d : all) if (d.netCfgInstanceId == UpperW(id.interfaceGuid)) { ++count; match = &d; }
    if (count == 0) return Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"VerifyIdentity/NoDevice");
    if (count > 1) return Status::Fail(APP_E_AMBIGUOUS_DEVICE, L"VerifyIdentity");
    if (UpperW(match->instanceId) != UpperW(id.deviceInstanceId) || match->driverKeyIndex != id.driverKeyIndex || match->driverKeyIndex.empty())
        return Status::Fail(APP_E_IDENTITY_MISMATCH, L"VerifyIdentity/Mismatch");
    // 2) Class 키의 NetCfgInstanceId 가 GUID 와 일치하는지
    HKEY h = nullptr;
    std::wstring path = std::wstring(L"SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}\\") + id.driverKeyIndex;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path.c_str(), 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &h) != ERROR_SUCCESS) return Status::Fail(APP_E_IDENTITY_MISMATCH, L"VerifyIdentity/ClassKey");
    wchar_t buf[64] = {}; DWORD cb = sizeof(buf) - 2, type = 0;
    LSTATUS r = RegQueryValueExW(h, L"NetCfgInstanceId", nullptr, &type, reinterpret_cast<BYTE*>(buf), &cb);
    RegCloseKey(h);
    if (r != ERROR_SUCCESS || type != REG_SZ || UpperW(buf) != UpperW(id.interfaceGuid)) return Status::Fail(APP_E_IDENTITY_MISMATCH, L"VerifyIdentity/NetCfgInstanceId");
    return Status::Ok();
}

Status WinAdapterControl::QueryState(const AdapterIdentity& id, AdapterState& out) {
    out = AdapterState{};
    HDEVINFO devs = INVALID_HANDLE_VALUE; SP_DEVINFO_DATA did{}; NetDeviceRecord rec;
    Status s = OpenAndCheck(id, devs, did, rec);
    if (s.code == APP_E_ADAPTER_NOT_FOUND) { out.present = false; return Status::Ok(); }
    if (!s.ok()) return s;
    SetupDiDestroyDeviceInfoList(devs);
    out.present = true;
    out.enabled = rec.enabled;
    out.rebootRequired = rec.rebootRequired;

    // IP Helper 로 현재 MAC/연결 상태
    ULONG flags = GAA_FLAG_INCLUDE_ALL_INTERFACES | GAA_FLAG_SKIP_UNICAST | GAA_FLAG_SKIP_ANYCAST | GAA_FLAG_SKIP_MULTICAST | GAA_FLAG_SKIP_DNS_SERVER;
    ULONG size = 32 * 1024;
    std::vector<BYTE> buf;
    for (int attempt = 0; attempt < 5; ++attempt) {
        buf.resize(size);
        ULONG r = GetAdaptersAddresses(AF_UNSPEC, flags, nullptr, reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buf.data()), &size);
        if (r == ERROR_BUFFER_OVERFLOW) continue;
        if (r == ERROR_NO_DATA) break;
        if (r != NO_ERROR) return Status::Fail(r, L"GetAdaptersAddresses");
        for (PIP_ADAPTER_ADDRESSES a = reinterpret_cast<PIP_ADAPTER_ADDRESSES>(buf.data()); a; a = a->Next) {
            std::string an = a->AdapterName ? a->AdapterName : "";
            if (UpperW(std::wstring(an.begin(), an.end())) != UpperW(id.interfaceGuid)) continue;
            out.interfaceVisible = true;
            out.operUp = a->OperStatus == IfOperStatusUp;
            if (a->PhysicalAddressLength == 6) { Mac m; for (int i = 0; i < 6; ++i) m.b[i] = a->PhysicalAddress[i]; out.currentMac = m; }
            break;
        }
        break;
    }
    // IP Helper 가 아직 인터페이스를 보이지 않으면 NDIS OID 로 보조 확인(드라이버가 올라온 직후)
    if (out.enabled && !out.currentMac) {
        Mac m; uint32_t e = 0;
        if (QueryNdisMac(id.interfaceGuid, 0x01010102 /*OID_802_3_CURRENT_ADDRESS*/, m, &e)) { out.currentMac = m; out.interfaceVisible = true; }
    }
    return Status::Ok();
}

Status WinAdapterControl::SetEnabled(const AdapterIdentity& id, bool enable, bool& rebootRequired) {
    rebootRequired = false;
    HDEVINFO devs = INVALID_HANDLE_VALUE; SP_DEVINFO_DATA did{}; NetDeviceRecord rec;
    Status s = OpenAndCheck(id, devs, did, rec);
    if (!s.ok()) return s;

    SP_PROPCHANGE_PARAMS pcp{};
    pcp.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
    pcp.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
    pcp.StateChange = enable ? DICS_ENABLE : DICS_DISABLE;
    pcp.Scope = DICS_FLAG_GLOBAL;
    pcp.HwProfile = 0;
    if (!SetupDiSetClassInstallParamsW(devs, &did, &pcp.ClassInstallHeader, sizeof(pcp))) {
        DWORD e = GetLastError();
        SetupDiDestroyDeviceInfoList(devs);
        return Status::Fail(e, L"SetupDiSetClassInstallParamsW");
    }
    if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devs, &did)) {
        DWORD e = GetLastError();
        SetupDiDestroyDeviceInfoList(devs);
        if (e == ERROR_IN_WOW64) return Status::Fail(APP_E_UNSUPPORTED_WOW64, L"SetupDiCallClassInstaller");
        return Status::Fail(e, L"SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)");
    }
    SP_DEVINSTALL_PARAMS_W dip{};
    dip.cbSize = sizeof(dip);
    if (SetupDiGetDeviceInstallParamsW(devs, &did, &dip)) rebootRequired = (dip.Flags & (DI_NEEDREBOOT | DI_NEEDRESTART)) != 0;
    SetupDiDestroyDeviceInfoList(devs);
    if (rebootRequired) return Status::Ok();   // 상태는 바뀌지 않았을 수 있음: 호출자가 플래그로 판단

    // 상태 도달 확인(최대 stateChangeTimeoutMs)
    ULONGLONG start = GetTickCount64();
    for (;;) {
        HDEVINFO d2 = INVALID_HANDLE_VALUE; SP_DEVINFO_DATA did2{}; NetDeviceRecord r2;
        Status q = OpenAndCheck(id, d2, did2, r2);
        if (q.ok()) {
            SetupDiDestroyDeviceInfoList(d2);
            if (r2.enabled == enable) return Status::Ok();
        } else if (q.code == APP_E_ADAPTER_NOT_FOUND) {
            return Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"SetEnabled/DeviceGone");
        }
        if (GetTickCount64() - start > stateChangeTimeoutMs) return Status::Fail(ERROR_TIMEOUT, L"SetEnabled/StateNotReached");
        Sleep(250);
    }
}

} // namespace macchg
