// Windows 전용 통합 테스트: 실제 레지스트리 API(WinRegistryBackend)를 HKCU 아래 격리 키에서 검증한다.
// HKLM 네트워크 설정은 전혀 건드리지 않는다. 장치 제어는 가짜(FakeAdapterControl)를 사용한다.
// 실행: macchg_integration_tests.exe  (관리자 권한 불필요)
#include <windows.h>
#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>
#include "fakes.h"
#include "core/engine.h"
#include "win/win_registry.h"

using namespace macchg;

static int g_fail = 0;
#define IT_CHECK(expr) do { if (!(expr)) { ++g_fail; std::printf("  FAIL %s:%d %s\n", __FILE__, __LINE__, #expr); } } while (0)

static std::wstring MakeBase() {
    wchar_t buf[128];
    swprintf(buf, 128, L"Software\\MacChangerTest\\run-%lu-%llu", GetCurrentProcessId(), GetTickCount64());
    return buf;
}

static bool CreateKeyPath(const std::wstring& path) {
    HKEY h = nullptr;
    LSTATUS r = RegCreateKeyExW(HKEY_CURRENT_USER, path.c_str(), 0, nullptr, 0, KEY_ALL_ACCESS, nullptr, &h, nullptr);
    if (r != ERROR_SUCCESS) return false;
    RegCloseKey(h);
    return true;
}

int wmain() {
    const std::wstring base = MakeBase();
    const std::wstring classBase = base + L"\\Class";
    const std::wstring tcpBase = base + L"\\Interfaces";
    const std::wstring guidA = L"{AAAAAAAA-1111-2222-3333-444444444444}";
    const std::wstring guidB = L"{BBBBBBBB-1111-2222-3333-444444444444}";
    std::printf("isolated root: HKCU\\%ls\n", base.c_str());

    if (!CreateKeyPath(classBase + L"\\0001") || !CreateKeyPath(classBase + L"\\0007") || !CreateKeyPath(tcpBase + L"\\" + guidA) || !CreateKeyPath(tcpBase + L"\\" + guidB) || !CreateKeyPath(tcpBase + L"\\" + guidA + L"\\SubKeyX")) {
        std::printf("FAIL: cannot create isolated keys\n");
        return 2;
    }

    WinRegistryBackend reg(HKEY_CURRENT_USER, classBase, tcpBase, 0);
    const RegKeyId classA = NetClassDriverKey(L"0001");
    const RegKeyId tcpA = TcpipInterfaceKey(guidA);
    const RegKeyId tcpB = TcpipInterfaceKey(guidB);

    // 잘못된 식별자(하위 키 탈출 시도)는 거부되어야 한다
    {
        bool exists = true;
        Status s = reg.KeyExists(RegKeyId{RegKeyKind::TcpipInterface, L"{AAAAAAAA-1111-2222-3333-444444444444}\\SubKeyX"}, exists);
        IT_CHECK(s.code == APP_E_IDENTITY_MISMATCH);
        s = reg.KeyExists(RegKeyId{RegKeyKind::NetClassDriver, L"..\\0001"}, exists);
        IT_CHECK(s.code == APP_E_IDENTITY_MISMATCH);
    }

    // 시드: 다양한 자료형, 기본값, 긴 값, 긴 이름
    std::vector<RegValue> seed = {
        MakeDwordValue(L"EnableDHCP", 1),
        MakeSzValue(L"DhcpIPAddress", L"192.168.0.23"),
        RegValue{L"IPAddress", RT_MULTI_SZ, fakes::World::EncodeMulti({L"0.0.0.0"})},
        RegValue{L"NameServer", RT_SZ, EncodeSz(L"")},
        RegValue{L"Lease", RT_DWORD, EncodeDword(86400)},
        RegValue{L"T1", RT_DWORD_BIG_ENDIAN, {0, 0, 0x54, 0x60}},
        RegValue{L"Q", RT_QWORD, {1, 2, 3, 4, 5, 6, 7, 8}},
        RegValue{L"Bin", RT_BINARY, std::vector<uint8_t>(70000, 0x5A)},            // 긴 값
        RegValue{std::wstring(16000, L'N'), RT_SZ, EncodeSz(L"long-name")},       // 긴 이름
        RegValue{L"", RT_SZ, EncodeSz(L"default-data")},                          // 기본값
        RegValue{L"NoTerm", RT_SZ, {0x41, 0x00, 0x42, 0x00}},                     // 널 종결 없는 REG_SZ
        RegValue{L"Odd", 0x1234, {9, 9, 9}},                                       // 알 수 없는 자료형
    };
    for (const auto& v : seed) IT_CHECK(reg.WriteValue(tcpA, v).ok());
    IT_CHECK(reg.WriteValue(tcpB, MakeDwordValue(L"EnableDHCP", 0)).ok());
    IT_CHECK(reg.WriteValue(tcpB, MakeSzValue(L"NameServer", L"1.1.1.1")).ok());
    IT_CHECK(reg.WriteValue(classA, MakeSzValue(L"NetCfgInstanceId", guidA)).ok());
    {
        HKEY sub = nullptr;
        RegOpenKeyExW(HKEY_CURRENT_USER, (tcpBase + L"\\" + guidA + L"\\SubKeyX").c_str(), 0, KEY_SET_VALUE, &sub);
        DWORD one = 1; RegSetValueExW(sub, L"Keep", 0, REG_DWORD, reinterpret_cast<BYTE*>(&one), sizeof(one)); RegCloseKey(sub);
    }

    // 열거가 바이트 단위로 정확한지
    std::vector<RegValue> snap;
    IT_CHECK(reg.EnumerateValues(tcpA, snap).ok());
    IT_CHECK(snap.size() == seed.size());
    for (const auto& s : seed) {
        bool found = false;
        for (const auto& v : snap) if (NameEqualsNoCase(v.name, s.name)) { found = true; IT_CHECK(v.type == s.type); IT_CHECK(v.data == s.data); }
        IT_CHECK(found);
    }

    // 엔진으로 변경 → 복구 (가짜 장치)
    fakes::FakeAdapterControl dev;
    AdapterIdentity A{guidA, L"PCI\\VEN_8086&DEV_15B8\\3&11583659&0&FE", L"0001"};
    Mac perm{{0x00, 0x11, 0x22, 0x33, 0x44, 0x55}};
    dev.Add(A, perm);
    fakes::FakeSystem sys;
    SessionBackupStore store;
    Engine e(reg, dev, sys, store);
    ChangeRequest req; req.identity = A; req.newMac = Mac{{0x0A, 0x1B, 0x2C, 0x3D, 0x4E, 0x5F}}; req.pollIntervalMs = 10; req.settleMs = 10; req.verifyTimeoutMs = 1000;
    // FakeAdapterControl 는 FakeRegistry 에서만 MAC 을 읽으므로 여기서는 드라이버가 값을 받아들였다고 가정하지 않고
    // '드라이버 무시 → 롤백' 경로가 실제 레지스트리에서 올바르게 동작하는지와, honors=false 로 두고 결과만 확인한다.
    dev.Find(guidA)->honorsNetworkAddress = false;
    Report r = e.Change(req, nullptr);
    IT_CHECK(r.outcome == Outcome::DriverIgnoredRolledBack);
    std::vector<RegValue> after;
    IT_CHECK(reg.EnumerateValues(tcpA, after).ok());
    IT_CHECK(after.size() == seed.size());
    for (const auto& s : seed) { bool ok = false; for (const auto& v : after) if (NameEqualsNoCase(v.name, s.name)) ok = (v.type == s.type && v.data == s.data); IT_CHECK(ok); }
    bool naFound = true; RegValue na;
    IT_CHECK(reg.ReadValue(classA, L"NetworkAddress", naFound, na).ok() && !naFound);

    // 정리만 직접 실행 → EnableDHCP 만 남고 하위 키/다른 키 불변 → 스냅샷 복원
    CleanupPlan plan = PlanTcpipCleanup(snap);
    std::vector<std::wstring> remaining; bool intact = false;
    IT_CHECK(ExecuteTcpipCleanup(reg, tcpA, plan, remaining, intact).ok());
    IT_CHECK(intact && remaining.empty());
    std::vector<RegValue> only;
    IT_CHECK(reg.EnumerateValues(tcpA, only).ok());
    IT_CHECK(only.size() == 1 && NameEqualsNoCase(only[0].name, L"EnableDHCP") && only[0].data == EncodeDword(1) && only[0].type == RT_DWORD);
    {
        HKEY sub = nullptr; DWORD v = 0, cb = sizeof(v), type = 0;
        IT_CHECK(RegOpenKeyExW(HKEY_CURRENT_USER, (tcpBase + L"\\" + guidA + L"\\SubKeyX").c_str(), 0, KEY_QUERY_VALUE, &sub) == ERROR_SUCCESS);
        IT_CHECK(RegQueryValueExW(sub, L"Keep", nullptr, &type, reinterpret_cast<BYTE*>(&v), &cb) == ERROR_SUCCESS && v == 1);
        RegCloseKey(sub);
        std::vector<RegValue> b; IT_CHECK(reg.EnumerateValues(tcpB, b).ok()); IT_CHECK(b.size() == 2);
    }
    std::vector<std::wstring> mism;
    IT_CHECK(RestoreValueSnapshot(reg, tcpA, snap, mism).ok() && mism.empty());
    std::vector<RegValue> restored;
    IT_CHECK(reg.EnumerateValues(tcpA, restored).ok());
    IT_CHECK(restored.size() == seed.size());
    for (const auto& s : seed) { bool ok = false; for (const auto& v : restored) if (NameEqualsNoCase(v.name, s.name)) ok = (v.type == s.type && v.data == s.data); IT_CHECK(ok); }

    // 정리
    RegDeleteTreeW(HKEY_CURRENT_USER, base.c_str());
    RegDeleteKeyW(HKEY_CURRENT_USER, L"Software\\MacChangerTest");
    std::printf("%s (%d failure(s))\n", g_fail == 0 ? "PASS" : "FAIL", g_fail);
    return g_fail == 0 ? 0 : 1;
}
