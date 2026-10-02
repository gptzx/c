#include "test_framework.h"
#include "fakes.h"
#include "core/restore_file.h"
#include "core/utf.h"

using namespace macchg;
using fakes::World;

static RestoreFile MakeFile(const World& w) {
    RestoreFile f;
    f.machine.machineGuid = L"12345678-ABCD-EF01-2345-6789ABCDEF01";
    f.machine.computerName = L"내-PC=테스트";
    f.createdAt = L"2026-10-02 12:00:00";
    f.appVersion = L"1.0.0";
    AdapterBackup& b = f.backup;
    b.identity = w.A;
    b.adapterName = L"이더넷 (Realtek) 🙂";
    b.adapterWasEnabled = true;
    b.macAtBackup = w.permA;
    b.networkAddressExisted = true;
    b.networkAddress = RegValue{L"NetworkAddress", RT_SZ, EncodeSz(L"021122334455")};
    b.tcpipKeyExisted = true;
    b.tcpipValues = {
        MakeDwordValue(L"EnableDHCP", 1),
        RegValue{L"", RT_SZ, EncodeSz(L"default")},
        RegValue{L"이름=값\r\n줄바꿈", RT_BINARY, std::vector<uint8_t>(3000, 0x7E)},
        RegValue{L"Multi", RT_MULTI_SZ, World::EncodeMulti({L"a", L"b"})},
        RegValue{L"Q", RT_QWORD, {8, 7, 6, 5, 4, 3, 2, 1}},
        RegValue{L"NoType", 0x1234, {}},   // 알 수 없는 자료형도 그대로 보존
    };
    return f;
}

static bool BackupEqual(const AdapterBackup& a, const AdapterBackup& b) {
    if (!(a.identity == b.identity)) return false;
    if (a.adapterName != b.adapterName || a.adapterWasEnabled != b.adapterWasEnabled) return false;
    if (a.macAtBackup.has_value() != b.macAtBackup.has_value()) return false;
    if (a.macAtBackup && *a.macAtBackup != *b.macAtBackup) return false;
    if (a.networkAddressExisted != b.networkAddressExisted) return false;
    if (a.networkAddressExisted && (a.networkAddress.type != b.networkAddress.type || a.networkAddress.data != b.networkAddress.data)) return false;
    if (a.tcpipKeyExisted != b.tcpipKeyExisted) return false;
    if (a.tcpipValues.size() != b.tcpipValues.size()) return false;
    for (size_t i = 0; i < a.tcpipValues.size(); ++i) if (a.tcpipValues[i] != b.tcpipValues[i]) return false;
    return true;
}

TEST(RestoreFile_RoundTripPreservesEverything) {
    World w;
    RestoreFile f = MakeFile(w);
    std::string bytes = SerializeRestoreFile(f);
    CHECK(bytes.rfind("MACCHG-RESTORE 1", 0) == 0);
    RestoreFile g; std::wstring err;
    Status s = ParseRestoreFile(bytes, g, err);
    CHECK(s.ok());
    CHECK(err.empty());
    CHECK_EQ(g.machine.machineGuid, f.machine.machineGuid);
    CHECK_EQ(g.machine.computerName, f.machine.computerName);
    CHECK(BackupEqual(f.backup, g.backup));
    // 재직렬화 결과 동일(결정적)
    CHECK_EQ(SerializeRestoreFile(g), bytes);
}

TEST(RestoreFile_RoundTripUnknownMacAndNoNetworkAddress) {
    World w;
    RestoreFile f = MakeFile(w);
    f.backup.macAtBackup.reset();
    f.backup.networkAddressExisted = false;
    f.backup.tcpipKeyExisted = false;
    f.backup.tcpipValues.clear();
    RestoreFile g; std::wstring err;
    REQUIRE(ParseRestoreFile(SerializeRestoreFile(f), g, err).ok());
    CHECK(BackupEqual(f.backup, g.backup));
}

TEST(RestoreFile_RejectsBadVersionTruncationAndCorruption) {
    World w;
    std::string good = SerializeRestoreFile(MakeFile(w));
    RestoreFile g; std::wstring err;
    // 버전
    std::string v2 = good; v2.replace(0, 16, "MACCHG-RESTORE 2");
    CHECK_EQ(ParseRestoreFile(v2, g, err).code, (uint32_t)APP_E_FILE_FORMAT);
    // 잘림(end 누락)
    std::string trunc = good.substr(0, good.size() - 5);
    CHECK_EQ(ParseRestoreFile(trunc, g, err).code, (uint32_t)APP_E_FILE_FORMAT);
    // 16진수 손상
    std::string bad = good; size_t p = bad.find("networkaddress.data="); REQUIRE(p != std::string::npos); bad[p + 20] = 'Z';
    CHECK_EQ(ParseRestoreFile(bad, g, err).code, (uint32_t)APP_E_FILE_FORMAT);
    // 다른 파일
    CHECK_EQ(ParseRestoreFile("hello\r\n", g, err).code, (uint32_t)APP_E_FILE_FORMAT);
    CHECK_EQ(ParseRestoreFile("", g, err).code, (uint32_t)APP_E_FILE_FORMAT);
    // 중복 값 이름
    std::string dup = good;
    size_t c = dup.find("tcpip.count=6"); REQUIRE(c != std::string::npos);
    dup.replace(c, 13, "tcpip.count=7");
    dup.insert(dup.find("end\r\n"), "tcpip.6.name=" + std::string("45006E0061006200") + "\r\ntcpip.6.type=4\r\ntcpip.6.data=01000000\r\n");  // "Enab" 아님 — 아래에서 EnableDHCP 와 동일 이름으로 교체
    RestoreFile g2;
    // 'ENABLEDHCP' 를 UTF-16LE hex 로
    std::string enHex; for (char ch : std::string("ENABLEDHCP")) { char buf[8]; std::snprintf(buf, sizeof(buf), "%02X00", (unsigned char)ch); enHex += buf; }
    dup.replace(dup.find("tcpip.6.name=") + 13, 16, enHex);
    CHECK_EQ(ParseRestoreFile(dup, g2, err).code, (uint32_t)APP_E_FILE_FORMAT);
}

TEST(RestoreFile_ValidateMachineAndAdapter) {
    World w;
    RestoreFile f = MakeFile(w);
    std::wstring err;
    MachineIdentity same{f.machine.machineGuid, L"other-name"};   // 이름은 검증에 쓰지 않음
    MachineIdentity other{L"00000000-0000-0000-0000-000000000000", L"x"};
    std::vector<AdapterIdentity> live{w.A, w.B};
    CHECK(ValidateRestoreFile(f, same, live, err).ok());
    CHECK_EQ(ValidateRestoreFile(f, other, live, err).code, (uint32_t)APP_E_FILE_MACHINE_MISMATCH);
    CHECK(!err.empty());
    // 어댑터 없음
    CHECK_EQ(ValidateRestoreFile(f, same, {w.B}, err).code, (uint32_t)APP_E_FILE_ADAPTER_MISMATCH);
    // GUID 같지만 인스턴스/드라이버 키 변경
    AdapterIdentity changed = w.A; changed.driverKeyIndex = L"0009";
    CHECK_EQ(ValidateRestoreFile(f, same, {changed, w.B}, err).code, (uint32_t)APP_E_FILE_ADAPTER_MISMATCH);
    AdapterIdentity changed2 = w.A; changed2.deviceInstanceId = L"USB\\VID_0BDA&PID_8153\\000001";
    CHECK_EQ(ValidateRestoreFile(f, same, {changed2}, err).code, (uint32_t)APP_E_FILE_ADAPTER_MISMATCH);
    // 같은 GUID 둘
    CHECK_EQ(ValidateRestoreFile(f, same, {w.A, changed}, err).code, (uint32_t)APP_E_AMBIGUOUS_DEVICE);
    // 빈 머신 GUID(읽기 실패)면 거부
    MachineIdentity empty{L"", L""};
    CHECK_EQ(ValidateRestoreFile(f, empty, live, err).code, (uint32_t)APP_E_FILE_MACHINE_MISMATCH);
}
