// fakes.h - 코어 테스트용 가짜 백엔드. 실제 레지스트리/장치를 전혀 건드리지 않는다.
#pragma once
#include <cstring>
#include <functional>
#include <map>
#include <set>
#include <string>
#include <vector>
#include "core/backend.h"
#include "core/backup.h"

namespace fakes {
using namespace macchg;

// 레지스트리 값 이름은 대소문자를 구분하지 않으므로 대문자 키로 저장하고 원래 표기는 RegValue.name 에 둔다.
inline std::wstring UpperW(const std::wstring& s) { std::wstring u = s; for (auto& c : u) c = static_cast<wchar_t>(std::towupper(static_cast<wint_t>(c))); return u; }

struct FakeKey {
    std::map<std::wstring, RegValue> values;             // UPPER(name) -> value
    std::map<std::wstring, FakeKey> subkeys;              // 하위 키(백엔드 인터페이스로는 접근 불가 — 불변 확인용)
};

class FakeRegistry : public IRegistryBackend {
public:
    std::map<RegKeyId, FakeKey> keys;

    // 실패 주입
    std::set<std::wstring> failDeleteNames;      // UPPER(name) 삭제 시 ERROR_ACCESS_DENIED(5)
    std::set<std::wstring> failWriteNames;       // UPPER(name) 기록 시 ERROR_ACCESS_DENIED(5)
    int failEnumerateAfter = -1;                 // n 번째 이후 열거 실패(-1: 없음)
    int failWriteAfterCount = -1;                // n 번째 이후 쓰기 실패
    std::function<void(FakeRegistry&)> onAfterEnumerate; // 열거 직후 콜백(불안정 스냅샷 시뮬레이션)
    int enumerateCalls = 0, writeCalls = 0, deleteCalls = 0;
    std::vector<std::wstring> log;

    FakeKey& Key(const RegKeyId& k) { return keys[k]; }
    void SetValue(const RegKeyId& k, const RegValue& v) { keys[k].values[UpperW(v.name)] = v; }
    void SetSz(const RegKeyId& k, const std::wstring& name, const std::wstring& s) { SetValue(k, MakeSzValue(name, s)); }
    void SetDword(const RegKeyId& k, const std::wstring& name, uint32_t d) { SetValue(k, MakeDwordValue(name, d)); }
    bool Has(const RegKeyId& k, const std::wstring& name) const { auto it = keys.find(k); return it != keys.end() && it->second.values.count(UpperW(name)); }
    const RegValue* Get(const RegKeyId& k, const std::wstring& name) const { auto it = keys.find(k); if (it == keys.end()) return nullptr; auto v = it->second.values.find(UpperW(name)); return v == it->second.values.end() ? nullptr : &v->second; }
    std::vector<RegValue> Values(const RegKeyId& k) const { std::vector<RegValue> out; auto it = keys.find(k); if (it != keys.end()) for (auto& kv : it->second.values) out.push_back(kv.second); return out; }

    Status KeyExists(const RegKeyId& key, bool& exists) override { exists = keys.count(key) != 0; return Status::Ok(); }
    Status EnumerateValues(const RegKeyId& key, std::vector<RegValue>& out) override {
        ++enumerateCalls;
        if (failEnumerateAfter >= 0 && enumerateCalls > failEnumerateAfter) return Status::Fail(5, L"FakeEnumerate");
        auto it = keys.find(key);
        if (it == keys.end()) return Status::Fail(2, L"FakeEnumerate/KeyMissing");
        out.clear();
        for (auto& kv : it->second.values) out.push_back(kv.second);
        if (onAfterEnumerate) onAfterEnumerate(*this);
        return Status::Ok();
    }
    Status ReadValue(const RegKeyId& key, const std::wstring& name, bool& found, RegValue& out) override {
        found = false;
        auto it = keys.find(key);
        if (it == keys.end()) return Status::Fail(2, L"FakeRead/KeyMissing");
        auto v = it->second.values.find(UpperW(name));
        if (v == it->second.values.end()) return Status::Ok();
        found = true; out = v->second; return Status::Ok();
    }
    Status WriteValue(const RegKeyId& key, const RegValue& value) override {
        ++writeCalls;
        log.push_back(L"W:" + key.id + L"\\" + value.name);
        if (failWriteNames.count(UpperW(value.name))) return Status::Fail(5, L"FakeWrite");
        if (failWriteAfterCount >= 0 && writeCalls > failWriteAfterCount) return Status::Fail(5, L"FakeWrite/Count");
        auto it = keys.find(key);
        if (it == keys.end()) return Status::Fail(2, L"FakeWrite/KeyMissing");
        it->second.values[UpperW(value.name)] = value;
        return Status::Ok();
    }
    Status DeleteValue(const RegKeyId& key, const std::wstring& name, bool& existed) override {
        ++deleteCalls;
        log.push_back(L"D:" + key.id + L"\\" + name);
        existed = false;
        if (failDeleteNames.count(UpperW(name))) return Status::Fail(5, L"FakeDelete");
        auto it = keys.find(key);
        if (it == keys.end()) return Status::Fail(2, L"FakeDelete/KeyMissing");
        existed = it->second.values.erase(UpperW(name)) > 0;
        return Status::Ok();
    }
};

// 장치 시뮬레이션. 활성화 시 레지스트리의 NetworkAddress 를 읽어 현재 MAC 을 결정한다(드라이버 동작 모사).
class FakeAdapterControl : public IAdapterControl {
public:
    struct Device {
        AdapterIdentity id;
        bool present = true;
        bool enabled = true;
        bool operUpWhenEnabled = true;
        Mac permanentMac;
        Mac currentMac;
        bool honorsNetworkAddress = true;   // false: 드라이버가 값을 무시
        bool only02Prefix = false;          // true: 첫 바이트 0x02 인 값만 받아들임(일부 무선랜 모사)
        bool rebootOnChange = false;        // SetEnabled 시 재부팅 필요 플래그 보고(장치는 상태 유지)
        int enableFailCount = 0;            // 앞에서 n 번 활성화 실패
        int disableFailCount = 0;
        int pollsUntilVisible = 0;          // 활성화 후 n 번 조회 동안 인터페이스 미표시
        bool vanishAfterDisable = false;    // 중지 후 장치가 사라짐(분리)
        bool rebootFlag = false;
        int pollCounter = 0;
    };
    std::map<std::wstring, Device> devices;  // UPPER(guid) -> device
    FakeRegistry* reg = nullptr;
    std::vector<std::wstring> log;
    int setEnabledCalls = 0;

    Device& Add(const AdapterIdentity& id, const Mac& permanent) {
        Device d; d.id = id; d.permanentMac = permanent; d.currentMac = permanent;
        devices[UpperW(id.interfaceGuid)] = d;
        return devices[UpperW(id.interfaceGuid)];
    }
    Device* Find(const std::wstring& guid) { auto it = devices.find(UpperW(guid)); return it == devices.end() ? nullptr : &it->second; }

    void RecomputeMac(Device& d) {
        // 드라이버 초기화 모사: NetworkAddress 가 있으면(그리고 받아들이면) 그 값을 현재 MAC 으로
        d.currentMac = d.permanentMac;
        if (!reg) return;
        const RegValue* v = reg->Get(NetClassDriverKey(d.id.driverKeyIndex), L"NetworkAddress");
        if (!v || !d.honorsNetworkAddress) return;
        if (v->type != RT_SZ) return;
        Mac m;
        if (!ParseMac(DecodeSz(v->data), m)) return;
        if (!IsUnicast(m)) return;
        if (d.only02Prefix && m.b[0] != 0x02) return;
        d.currentMac = m;
    }

    Status VerifyIdentity(const AdapterIdentity& id) override {
        Device* d = Find(id.interfaceGuid);
        if (!d || !d->present) return Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"FakeVerifyIdentity");
        if (d->id != id) return Status::Fail(APP_E_IDENTITY_MISMATCH, L"FakeVerifyIdentity");
        return Status::Ok();
    }
    Status QueryState(const AdapterIdentity& id, AdapterState& out) override {
        out = AdapterState{};
        Device* d = Find(id.interfaceGuid);
        if (!d || !d->present) { out.present = false; return Status::Ok(); }
        out.present = true;
        out.enabled = d->enabled;
        out.rebootRequired = d->rebootFlag;
        if (d->enabled) {
            if (d->pollCounter < d->pollsUntilVisible) { ++d->pollCounter; out.interfaceVisible = false; return Status::Ok(); }
            out.interfaceVisible = true;
            out.currentMac = d->currentMac;
            out.operUp = d->operUpWhenEnabled;
        }
        return Status::Ok();
    }
    Status SetEnabled(const AdapterIdentity& id, bool enable, bool& rebootRequired) override {
        ++setEnabledCalls;
        rebootRequired = false;
        Device* d = Find(id.interfaceGuid);
        if (!d || !d->present) return Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"FakeSetEnabled");
        log.push_back((enable ? L"ENABLE:" : L"DISABLE:") + id.interfaceGuid);
        if (enable) {
            if (d->enableFailCount > 0) { --d->enableFailCount; return Status::Fail(1460 /*ERROR_TIMEOUT*/, L"FakeSetEnabled/Enable"); }
            if (d->rebootOnChange) { d->rebootFlag = true; rebootRequired = true; return Status::Ok(); }  // 상태 변화 없이 재부팅 필요만 보고
            d->enabled = true;
            d->pollCounter = 0;
            RecomputeMac(*d);
        } else {
            if (d->disableFailCount > 0) { --d->disableFailCount; return Status::Fail(5, L"FakeSetEnabled/Disable"); }
            if (d->rebootOnChange) { d->rebootFlag = true; rebootRequired = true; return Status::Ok(); }
            d->enabled = false;
            if (d->vanishAfterDisable) d->present = false;
        }
        return Status::Ok();
    }
};

class FakeRandom : public IRandomSource {
public:
    bool fail = false;
    std::vector<uint8_t> scripted;   // 비어 있으면 결정적 LCG
    uint32_t state = 0x12345678;
    size_t pos = 0;
    bool Fill(uint8_t* buf, size_t len) override {
        if (fail) return false;
        for (size_t i = 0; i < len; ++i) {
            if (!scripted.empty()) { buf[i] = scripted[pos % scripted.size()]; ++pos; }
            else { state = state * 1664525u + 1013904223u; buf[i] = static_cast<uint8_t>(state >> 24); }
        }
        return true;
    }
};

class FakeSystem : public ISystem {
public:
    uint64_t now = 1000;
    uint64_t sleepTotal = 0;
    uint64_t NowMs() override { return now; }
    void SleepMs(uint32_t ms) override { now += ms; sleepTotal += ms; }
};

// 공통 픽스처: 어댑터 2개(A: 대상, B: 다른 어댑터) + 클래스 키 + Tcpip 키 + 하위 키
struct World {
    FakeRegistry reg;
    FakeAdapterControl dev;
    FakeRandom rng;
    FakeSystem sys;
    SessionBackupStore store;
    AdapterIdentity A{L"{AAAAAAAA-1111-2222-3333-444444444444}", L"PCI\\VEN_8086&DEV_15B8\\3&11583659&0&FE", L"0001"};
    AdapterIdentity B{L"{BBBBBBBB-1111-2222-3333-444444444444}", L"PCI\\VEN_8086&DEV_2723\\4&2A3B4C5D&0&00E0", L"0007"};
    Mac permA{{0x00, 0x11, 0x22, 0x33, 0x44, 0x55}};
    Mac permB{{0x00, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE}};
    RegKeyId classA = NetClassDriverKey(L"0001");
    RegKeyId classB = NetClassDriverKey(L"0007");
    RegKeyId tcpA = TcpipInterfaceKey(L"{AAAAAAAA-1111-2222-3333-444444444444}");
    RegKeyId tcpB = TcpipInterfaceKey(L"{BBBBBBBB-1111-2222-3333-444444444444}");

    World() {
        dev.reg = &reg;
        dev.Add(A, permA);
        dev.Add(B, permB);
        reg.SetSz(classA, L"NetCfgInstanceId", A.interfaceGuid);
        reg.SetSz(classA, L"DriverDesc", L"Intel(R) Ethernet Connection");
        reg.SetSz(classB, L"NetCfgInstanceId", B.interfaceGuid);
        reg.SetSz(classB, L"DriverDesc", L"Intel(R) Wi-Fi 6 AX200");
        // 대상 어댑터의 Tcpip 값: 다양한 자료형 + 기본값 + 긴 값
        reg.SetDword(tcpA, L"EnableDHCP", 1);
        reg.SetSz(tcpA, L"DhcpIPAddress", L"192.168.0.23");
        reg.SetValue(tcpA, RegValue{L"IPAddress", RT_MULTI_SZ, EncodeMulti({L"0.0.0.0"})});
        reg.SetValue(tcpA, RegValue{L"SubnetMask", RT_MULTI_SZ, EncodeMulti({L"0.0.0.0"})});
        reg.SetValue(tcpA, RegValue{L"DefaultGateway", RT_MULTI_SZ, EncodeMulti({})});
        reg.SetSz(tcpA, L"NameServer", L"");
        reg.SetValue(tcpA, RegValue{L"Lease", RT_DWORD, EncodeDword(86400)});
        reg.SetValue(tcpA, RegValue{L"LeaseObtainedTime", RT_DWORD, EncodeDword(1700000000)});
        reg.SetValue(tcpA, RegValue{L"DhcpGatewayHardware", RT_BINARY, std::vector<uint8_t>(64, 0xA5)});
        reg.SetValue(tcpA, RegValue{L"DhcpInterfaceOptions", RT_BINARY, std::vector<uint8_t>(5000, 0x5A)});  // 긴 값
        reg.SetValue(tcpA, RegValue{L"T1", RT_DWORD_BIG_ENDIAN, {0, 0, 0x54, 0x60}});
        reg.SetValue(tcpA, RegValue{L"SomeQword", RT_QWORD, {1, 2, 3, 4, 5, 6, 7, 8}});
        reg.SetValue(tcpA, RegValue{L"", RT_SZ, EncodeSz(L"default-data")});  // 이름 없는 기본값(데이터 있음)
        reg.Key(tcpA).subkeys[L"SubKeyX"].values[L"KEEP"] = MakeSzValue(L"Keep", L"1");
        // 다른 어댑터의 Tcpip 값
        reg.SetDword(tcpB, L"EnableDHCP", 0);
        reg.SetValue(tcpB, RegValue{L"IPAddress", RT_MULTI_SZ, EncodeMulti({L"10.0.0.5"})});
        reg.SetSz(tcpB, L"NameServer", L"1.1.1.1");
    }

    static std::vector<uint8_t> EncodeMulti(const std::vector<std::wstring>& items) {
        std::vector<uint8_t> out;
        for (const auto& s : items) { auto e = EncodeSz(s); out.insert(out.end(), e.begin(), e.end()); }
        out.push_back(0); out.push_back(0);
        return out;
    }
    std::map<RegKeyId, FakeKey> Snapshot() const { return reg.keys; }
};

inline bool SameValues(const std::map<std::wstring, RegValue>& a, const std::map<std::wstring, RegValue>& b) {
    if (a.size() != b.size()) return false;
    for (auto& kv : a) { auto it = b.find(kv.first); if (it == b.end() || it->second != kv.second) return false; }
    return true;
}

} // namespace fakes
