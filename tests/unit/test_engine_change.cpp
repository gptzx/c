#include <atomic>
#include "test_framework.h"
#include "fakes.h"
#include "core/engine.h"

using namespace macchg;
using fakes::World;

static Mac M(const wchar_t* s) { Mac m; ParseMac(s, m); return m; }

static ChangeRequest ReqFor(const World& w, const wchar_t* mac) {
    ChangeRequest r;
    r.identity = w.A;
    r.displayName = L"이더넷";
    r.newMac = M(mac);
    r.verifyTimeoutMs = 5000; r.pollIntervalMs = 100; r.settleMs = 300;
    return r;
}

static std::vector<Phase> phases;
static void Prog(Phase p, const std::wstring&) { phases.push_back(p); }

TEST(Change_HappyPath_WritesRegSz_CleansTcpip_VerifiesMac_RecordsBaseline) {
    World w;
    auto before = w.Snapshot();
    Engine e(w.reg, w.dev, w.sys, w.store);
    phases.clear();
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), Prog);
    CHECK_EQ((int)r.outcome, (int)Outcome::Success);
    CHECK(r.observedMac && *r.observedMac == M(L"0A1B2C3D4E5F"));
    CHECK(r.previousMac && *r.previousMac == w.permA);
    CHECK(r.operUp);
    CHECK(r.baselineRecorded);
    // NetworkAddress: 구분자 없는 12자리 REG_SZ
    const RegValue* na = w.reg.Get(w.classA, L"NetworkAddress");
    REQUIRE(na != nullptr);
    CHECK_EQ(na->type, (uint32_t)RT_SZ);
    CHECK_EQ(DecodeSz(na->data), std::wstring(L"0A1B2C3D4E5F"));
    // Tcpip: EnableDHCP 만 남음, 값 동일
    auto vals = w.reg.Values(w.tcpA);
    REQUIRE(vals.size() == 1);
    CHECK(vals[0] == before[w.tcpA].values[L"ENABLEDHCP"]);
    CHECK_EQ(r.deletedValues.size(), (size_t)12);
    CHECK(r.enableDhcpPreserved && r.enableDhcpIntact);
    // 다른 어댑터·하위 키·클래스 키 B 불변
    CHECK(fakes::SameValues(w.reg.Key(w.tcpB).values, before[w.tcpB].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classB).values, before[w.classB].values));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).subkeys[L"SubKeyX"].values, before[w.tcpA].subkeys[L"SubKeyX"].values));
    // 장치 흐름: DISABLE → ENABLE 각각 1회, 다른 어댑터는 건드리지 않음
    REQUIRE(w.dev.log.size() == 2);
    CHECK_EQ(w.dev.log[0], L"DISABLE:" + w.A.interfaceGuid);
    CHECK_EQ(w.dev.log[1], L"ENABLE:" + w.A.interfaceGuid);
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(w.dev.Find(w.B.interfaceGuid)->currentMac == w.permB);
    // 세션 기준: 변경 전 상태가 기록됨
    REQUIRE(w.store.HasOriginal(w.A.interfaceGuid));
    const AdapterBackup* b = w.store.Original(w.A.interfaceGuid);
    CHECK(!b->networkAddressExisted);
    CHECK_EQ(b->tcpipValues.size(), (size_t)13);
    CHECK(b->adapterWasEnabled);
    CHECK(b->macAtBackup && *b->macAtBackup == w.permA);
    // 단계 순서
    REQUIRE(phases.size() >= 7);
    CHECK((int)phases[0] == (int)Phase::Validating);
    CHECK((int)phases[1] == (int)Phase::BackingUp);
    CHECK((int)phases[2] == (int)Phase::Disabling);
    CHECK((int)phases[3] == (int)Phase::Applying);
    CHECK((int)phases[4] == (int)Phase::CleaningTcpip);
    CHECK((int)phases[5] == (int)Phase::Enabling);
    CHECK((int)phases[6] == (int)Phase::Verifying);
}

TEST(Change_Repeated_KeepsFirstBaseline_RestoreReturnsToInitial) {
    World w;
    auto before = w.Snapshot();
    Engine e(w.reg, w.dev, w.sys, w.store);
    CHECK_EQ((int)e.Change(ReqFor(w, L"0A0000000001"), nullptr).outcome, (int)Outcome::Success);
    // 두 번째 변경 전, DHCP 클라이언트가 값을 다시 만든 상황을 흉내낸다
    w.reg.SetSz(w.tcpA, L"DhcpIPAddress", L"192.168.0.77");
    w.reg.SetDword(w.tcpA, L"Lease", 3600);
    Report r2 = e.Change(ReqFor(w, L"0E0000000002"), nullptr);
    CHECK_EQ((int)r2.outcome, (int)Outcome::Success);
    CHECK(!r2.baselineRecorded);
    Report r3 = e.Change(ReqFor(w, L"060000000003"), nullptr);
    CHECK_EQ((int)r3.outcome, (int)Outcome::Success);
    // 기준은 최초 것 그대로
    const AdapterBackup* b = w.store.Original(w.A.interfaceGuid);
    REQUIRE(b != nullptr);
    CHECK(!b->networkAddressExisted);
    CHECK_EQ(b->tcpipValues.size(), (size_t)13);
    // 원상복구
    RestoreRequest rr; rr.identity = w.A; rr.baseline = *b; rr.verifyTimeoutMs = 5000; rr.pollIntervalMs = 100; rr.settleMs = 300;
    Report rs = e.Restore(rr, nullptr);
    CHECK_EQ((int)rs.outcome, (int)Outcome::Restored);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, before[w.classA].values));
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(w.dev.Find(w.A.interfaceGuid)->currentMac == w.permA);
    CHECK(!w.store.HasOriginal(w.A.interfaceGuid));
    CHECK(rs.baselineCleared);
}

TEST(Change_ExistingNetworkAddressIsRestoredExactly) {
    World w;
    // 기존 NetworkAddress 가 REG_SZ 가 아닌 형태(예: 소문자, 다른 자료형)로 있어도 그대로 복원해야 한다
    RegValue odd{L"NetworkAddress", RT_BINARY, {0x02, 0xAB, 0xCD, 0xEF, 0x00, 0x01}};
    w.reg.SetValue(w.classA, odd);
    Engine e(w.reg, w.dev, w.sys, w.store);
    CHECK_EQ((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome, (int)Outcome::Success);
    const AdapterBackup* b = w.store.Original(w.A.interfaceGuid);
    REQUIRE(b && b->networkAddressExisted);
    CHECK(b->networkAddress.type == RT_BINARY && b->networkAddress.data == odd.data);
    RestoreRequest rr; rr.identity = w.A; rr.baseline = *b; rr.pollIntervalMs = 100; rr.settleMs = 100; rr.verifyTimeoutMs = 3000;
    e.Restore(rr, nullptr);
    const RegValue* na = w.reg.Get(w.classA, L"NetworkAddress");
    REQUIRE(na != nullptr);
    CHECK(na->type == RT_BINARY && na->data == odd.data);
}

TEST(Change_OriginallyDisabledAdapter_StaysDisabled_NotReportedAsSuccess) {
    World w;
    w.dev.Find(w.A.interfaceGuid)->enabled = false;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::AppliedAdapterDisabled);
    CHECK(!w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(w.dev.log.empty());   // 중지/활성화 호출 없음
    CHECK(w.reg.Has(w.classA, L"NetworkAddress"));
    const AdapterBackup* b = w.store.Original(w.A.interfaceGuid);
    REQUIRE(b != nullptr);
    CHECK(!b->adapterWasEnabled);
    CHECK(!b->macAtBackup.has_value());
    RestoreRequest rr; rr.identity = w.A; rr.baseline = *b;
    Report rs = e.Restore(rr, nullptr);
    CHECK_EQ((int)rs.outcome, (int)Outcome::RestoredUnverified);
    CHECK(!w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
}

TEST(Change_DriverIgnoresValue_NoFalseSuccess_RolledBack) {
    World w;
    auto before = w.Snapshot();
    w.dev.Find(w.A.interfaceGuid)->honorsNetworkAddress = false;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::DriverIgnoredRolledBack);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_DRIVER_IGNORED);
    CHECK(r.observedMac && *r.observedMac == w.permA);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, before[w.classA].values));
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(!w.store.HasOriginal(w.A.interfaceGuid));  // 최초 작업이 완전 복구되었으므로 기준 제거
    CHECK(r.baselineCleared);
}

TEST(Change_DriverIgnoresAfterEarlierSuccess_RollsBackToPreviousChangedState_KeepsBaseline) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    CHECK_EQ((int)e.Change(ReqFor(w, L"0A0000000001"), nullptr).outcome, (int)Outcome::Success);
    auto mid = w.Snapshot();
    w.dev.Find(w.A.interfaceGuid)->only02Prefix = true;   // 이제부터 02 로 시작하는 값만 받아들임
    Report r = e.Change(ReqFor(w, L"0E0000000002"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::DriverIgnoredRolledBack);
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, mid[w.classA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, mid[w.tcpA].values));
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));
    // 02 고정이면 성공
    Report r2 = e.Change(ReqFor(w, L"020000000003"), nullptr);
    CHECK_EQ((int)r2.outcome, (int)Outcome::Success);
}

TEST(Change_RebootRequiredFlag_IsReportedNotSuccess) {
    World w;
    w.dev.Find(w.A.interfaceGuid)->rebootOnChange = true;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::RebootRequired);
    CHECK(r.rebootRequired);
    CHECK(w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));  // 복구 기준 유지
}

TEST(Change_DisableFails_NothingChanged) {
    World w;
    auto before = w.Snapshot();
    w.dev.Find(w.A.interfaceGuid)->disableFailCount = 1;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::NothingChanged);
    CHECK_EQ((int)r.failedStep, (int)Step::Disable);
    CHECK_EQ(r.error.code, (uint32_t)5);
    CHECK_EQ(w.reg.writeCalls, 0);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(!w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Change_WriteNetworkAddressFails_RollsBack) {
    World w;
    auto before = w.Snapshot();
    w.reg.failWriteNames.insert(L"NETWORKADDRESS");
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::FailedRolledBack);
    CHECK_EQ((int)r.failedStep, (int)Step::WriteNetworkAddress);
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, before[w.classA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(!w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Change_CleanupPartialFailure_RestoresDeletedValues_AndNetworkAddress) {
    World w;
    auto before = w.Snapshot();
    w.reg.failDeleteNames.insert(L"LEASE");
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::FailedRolledBack);
    CHECK_EQ((int)r.failedStep, (int)Step::CleanupTcpip);
    CHECK_EQ(r.error.code, (uint32_t)5);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));   // 삭제했던 값 전부 복원
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(w.dev.Find(w.A.interfaceGuid)->currentMac == w.permA);
    CHECK(r.remainingChanges.empty());
}

TEST(Change_EnableFailsOnce_RollbackReenables) {
    World w;
    auto before = w.Snapshot();
    w.dev.Find(w.A.interfaceGuid)->enableFailCount = 1;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::FailedRolledBack);
    CHECK_EQ((int)r.failedStep, (int)Step::Enable);
    CHECK_EQ(r.error.code, (uint32_t)1460);
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
}

TEST(Change_EnableFailsPersistently_RollbackFailed_ReportsRemaining) {
    World w;
    w.dev.Find(w.A.interfaceGuid)->enableFailCount = 10;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::FailedRollbackFailed);
    CHECK(!r.rollbackError.ok());
    CHECK(!r.remainingChanges.empty());
    CHECK(!w.dev.Find(w.A.interfaceGuid)->enabled);
    // 레지스트리는 되돌려졌지만 장치는 중지 상태 → 기준은 유지(사용자가 다시 복구 시도 가능)
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Change_DeviceVanishesAfterDisable_ReportsNotFound) {
    World w;
    w.dev.Find(w.A.interfaceGuid)->vanishAfterDisable = true;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK((int)r.outcome == (int)Outcome::FailedRollbackFailed);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_ADAPTER_NOT_FOUND);
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));   // 레지스트리는 되돌림
    CHECK(!r.remainingChanges.empty());
}

TEST(Change_VerifyTimeout_NotSuccess_ChangesKept) {
    World w;
    w.dev.Find(w.A.interfaceGuid)->pollsUntilVisible = 100000;
    Engine e(w.reg, w.dev, w.sys, w.store);
    ChangeRequest req = ReqFor(w, L"0A1B2C3D4E5F");
    req.verifyTimeoutMs = 2000;
    Report r = e.Change(req, nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::VerifyTimeout);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_VERIFY_TIMEOUT);
    CHECK(!r.observedMac.has_value());
    CHECK(w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));
    CHECK(w.sys.sleepTotal >= 2000);
}

TEST(Change_CancelBeforeWrite_RollsBack_ReenablesAdapter) {
    World w;
    auto before = w.Snapshot();
    Engine e(w.reg, w.dev, w.sys, w.store);
    std::atomic<bool> cancel{false};
    ChangeRequest req = ReqFor(w, L"0A1B2C3D4E5F");
    req.cancel = &cancel;
    Report r = e.Change(req, [&](Phase p, const std::wstring&) { if (p == Phase::Disabling) cancel.store(true); });
    CHECK_EQ((int)r.outcome, (int)Outcome::CancelledRolledBack);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_CANCELLED);
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(!w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Change_IdentityMismatch_NoWritesNoDeviceCalls) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    ChangeRequest req = ReqFor(w, L"0A1B2C3D4E5F");
    req.identity.driverKeyIndex = L"0002";   // GUID 는 맞지만 드라이버 키가 다름
    Report r = e.Change(req, nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::NothingChanged);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_IDENTITY_MISMATCH);
    CHECK_EQ(w.reg.writeCalls, 0);
    CHECK_EQ(w.reg.deleteCalls, 0);
    CHECK_EQ(w.dev.setEnabledCalls, 0);
}

TEST(Change_InvalidMac_NothingChanged) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"010000000001"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::NothingChanged);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_INVALID_MAC);
    CHECK_EQ(w.dev.setEnabledCalls, 0);
}

TEST(Change_UnstableBackupSnapshot_Aborts) {
    World w;
    int n = 0;
    w.reg.onAfterEnumerate = [&](fakes::FakeRegistry& r) { r.SetDword(TcpipInterfaceKey(L"{AAAAAAAA-1111-2222-3333-444444444444}"), L"Lease", static_cast<uint32_t>(++n)); };
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::NothingChanged);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_BACKUP_INCOMPLETE);
    CHECK_EQ(w.dev.setEnabledCalls, 0);
    CHECK_EQ(w.reg.deleteCalls, 0);
}

TEST(Change_TcpipKeyMissing_StillChangesMac) {
    World w;
    w.reg.keys.erase(w.tcpA);
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::Success);
    CHECK(r.deletedValues.empty());
    CHECK(!w.store.Original(w.A.interfaceGuid)->tcpipKeyExisted);
    CHECK(w.reg.keys.count(w.tcpA) == 0);   // 키를 만들지 않음
}

TEST(Change_EnableDhcpZero_IsKeptZero_AndStaticSettingsReported) {
    World w;
    w.reg.SetDword(w.tcpA, L"EnableDHCP", 0);
    w.reg.SetValue(w.tcpA, RegValue{L"IPAddress", RT_MULTI_SZ, World::EncodeMulti({L"192.168.0.10"})});
    w.reg.SetSz(w.tcpA, L"NameServer", L"8.8.8.8,1.1.1.1");
    Engine e(w.reg, w.dev, w.sys, w.store);
    Report r = e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::Success);
    const RegValue* d = w.reg.Get(w.tcpA, L"EnableDHCP");
    REQUIRE(d != nullptr);
    uint32_t v = 99; CHECK(DecodeDword(d->data, v)); CHECK_EQ(v, (uint32_t)0);
    CHECK(r.staticSettings.dhcpDisabled);
    CHECK_EQ(r.staticSettings.items.size(), (size_t)2);
    CHECK_EQ(w.reg.Values(w.tcpA).size(), (size_t)1);
}

TEST(Change_RemoveNetworkAddressMode_FactoryMac_NoTcpipCleanup) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    CHECK_EQ((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome, (int)Outcome::Success);
    // DHCP 재생성 흉내
    w.reg.SetSz(w.tcpA, L"DhcpIPAddress", L"192.168.0.50");
    auto mid = w.Snapshot();
    ChangeRequest rm; rm.identity = w.A; rm.mode = ChangeRequest::Mode::RemoveNetworkAddress; rm.cleanupTcpip = false;
    rm.expectedMacAfterRemove = w.permA; rm.pollIntervalMs = 100; rm.settleMs = 100; rm.verifyTimeoutMs = 3000;
    Report r = e.Change(rm, nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::Success);
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, mid[w.tcpA].values));   // TCP/IP 값은 건드리지 않음
    CHECK(r.deletedValues.empty());
    // 세션 기준은 여전히 최초 상태(13개 값)
    CHECK_EQ(w.store.Original(w.A.interfaceGuid)->tcpipValues.size(), (size_t)13);
    // 영구 MAC 을 모르면 검증 불가로 보고
    e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr);
    rm.expectedMacAfterRemove.reset();
    Report r2 = e.Change(rm, nullptr);
    CHECK_EQ((int)r2.outcome, (int)Outcome::AppliedUnverified);
}
