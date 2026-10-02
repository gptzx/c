#include "cleanup.h"
#include "utf.h"

namespace macchg {

const wchar_t* const kEnableDhcpName = L"EnableDHCP";

CleanupPlan PlanTcpipCleanup(const std::vector<RegValue>& snapshot) {
    CleanupPlan p;
    for (const RegValue& v : snapshot) {
        if (NameEqualsNoCase(v.name, kEnableDhcpName)) {
            // 레지스트리 값 이름은 대소문자를 구분하지 않으므로 하나만 존재한다.
            p.enableDhcpPresent = true;
            p.enableDhcp = v;
            continue;
        }
        p.toDelete.push_back(v);
    }
    return p;
}

static std::vector<std::wstring> DecodeMultiSz(const std::vector<uint8_t>& data) {
    std::vector<std::wstring> out;
    std::vector<uint16_t> buf;
    for (size_t i = 0; i + 1 < data.size(); i += 2) {
        uint16_t u = static_cast<uint16_t>(data[i] | (data[i + 1] << 8));
        if (u == 0) { if (buf.empty()) break; out.push_back(FromUtf16Units(buf)); buf.clear(); }
        else buf.push_back(u);
    }
    if (!buf.empty()) out.push_back(FromUtf16Units(buf));
    return out;
}

// Windows 는 DHCP 어댑터에도 IPAddress/SubnetMask = "0.0.0.0" 을 둔다. 이는 고정 설정이 아니다.
static bool IsPlaceholder(const std::wstring& s) { return s.empty() || s == L"0.0.0.0"; }

static bool IsEmptyStringData(const RegValue& v) {
    if (v.type == RT_SZ || v.type == RT_EXPAND_SZ) return IsPlaceholder(DecodeSz(v.data));
    if (v.type == RT_MULTI_SZ) {
        for (const std::wstring& s : DecodeMultiSz(v.data)) if (!IsPlaceholder(s)) return false;
        return true;
    }
    return v.data.empty();
}

StaticSettingsSummary DetectStaticSettings(const std::vector<RegValue>& snapshot) {
    StaticSettingsSummary s;
    static const wchar_t* const kStaticNames[] = {L"IPAddress", L"SubnetMask", L"DefaultGateway", L"NameServer", L"Domain", L"SearchList", L"DhcpNameServer"};
    for (const RegValue& v : snapshot) {
        if (NameEqualsNoCase(v.name, kEnableDhcpName)) {
            uint32_t d = 0;
            if (DecodeDword(v.data, d) && d == 0) s.dhcpDisabled = true;
            continue;
        }
        for (const wchar_t* n : kStaticNames) {
            if (NameEqualsNoCase(v.name, n)) {
                if (NameEqualsNoCase(v.name, L"DhcpNameServer")) break;  // DHCP가 준 값은 고정 설정이 아님
                if (!IsEmptyStringData(v)) s.items.push_back(v.name + L" = " + SummarizeRegData(v));
                break;
            }
        }
    }
    return s;
}

Status VerifyTcpipCleanup(IRegistryBackend& reg, const RegKeyId& key, const CleanupPlan& plan, std::vector<std::wstring>& remaining, bool& preservedIntact) {
    remaining.clear();
    preservedIntact = true;
    std::vector<RegValue> now;
    Status s = reg.EnumerateValues(key, now);
    if (!s.ok()) return s;
    for (const RegValue& v : now) {
        if (NameEqualsNoCase(v.name, kEnableDhcpName)) {
            if (plan.enableDhcpPresent) {
                if (v.type != plan.enableDhcp.type || v.data != plan.enableDhcp.data) preservedIntact = false;
            }
            // 원래 없었는데 생겼다면 우리가 만든 것이 아니다(만들지 않음). 실패로 보지 않는다.
            continue;
        }
        remaining.push_back(v.name);
    }
    if (!remaining.empty()) return Status::Fail(APP_E_CLEANUP_INCOMPLETE, L"VerifyTcpipCleanup");
    return Status::Ok();
}

Status ExecuteTcpipCleanup(IRegistryBackend& reg, const RegKeyId& key, const CleanupPlan& plan, std::vector<std::wstring>& remaining, bool& preservedIntact) {
    remaining.clear();
    preservedIntact = true;
    Status firstError = Status::Ok();
    // 스냅샷(계획)을 기준으로 삭제한다. 열거와 삭제를 동시에 하지 않는다.
    for (const RegValue& v : plan.toDelete) {
        bool existed = false;
        Status s = reg.DeleteValue(key, v.name, existed);
        if (!s.ok() && firstError.ok()) firstError = s;
    }
    // 계획 시점 이후 다른 프로세스가 만든 값도 포함해 "EnableDHCP 외 값 없음"을 재열거로 검증한다.
    Status v = VerifyTcpipCleanup(reg, key, plan, remaining, preservedIntact);
    if (!v.ok()) {
        if (!firstError.ok()) return firstError;  // 실제 삭제 오류 코드를 우선 보고
        return v;
    }
    if (!firstError.ok()) return firstError;  // 삭제 오류가 있었지만 값이 사라진 경우도 성공으로 보지 않음
    return Status::Ok();
}

Status RestoreValueSnapshot(IRegistryBackend& reg, const RegKeyId& key, const std::vector<RegValue>& snapshot, std::vector<std::wstring>& mismatches) {
    mismatches.clear();
    std::vector<RegValue> now;
    Status s = reg.EnumerateValues(key, now);
    if (!s.ok()) return s;
    Status firstError = Status::Ok();
    // 1) 스냅샷에 없는 값 삭제
    for (const RegValue& cur : now) {
        bool inSnap = false;
        for (const RegValue& sv : snapshot) if (NameEqualsNoCase(sv.name, cur.name)) { inSnap = true; break; }
        if (!inSnap) {
            bool existed = false;
            Status d = reg.DeleteValue(key, cur.name, existed);
            if (!d.ok() && firstError.ok()) firstError = d;
        }
    }
    // 2) 스냅샷 값 기록(자료형·원시 데이터 그대로)
    for (const RegValue& sv : snapshot) {
        Status w = reg.WriteValue(key, sv);
        if (!w.ok() && firstError.ok()) firstError = w;
    }
    // 3) 검증
    std::vector<RegValue> after;
    s = reg.EnumerateValues(key, after);
    if (!s.ok()) return firstError.ok() ? s : firstError;
    for (const RegValue& sv : snapshot) {
        bool ok = false;
        for (const RegValue& av : after) if (NameEqualsNoCase(av.name, sv.name)) { ok = (av.type == sv.type && av.data == sv.data); break; }
        if (!ok) mismatches.push_back(sv.name);
    }
    for (const RegValue& av : after) {
        bool inSnap = false;
        for (const RegValue& sv : snapshot) if (NameEqualsNoCase(sv.name, av.name)) { inSnap = true; break; }
        if (!inSnap) mismatches.push_back(av.name + L" (extra)");
    }
    if (!firstError.ok()) return firstError;
    if (!mismatches.empty()) return Status::Fail(APP_E_RESTORE_MISMATCH, L"RestoreValueSnapshot");
    return Status::Ok();
}

} // namespace macchg
