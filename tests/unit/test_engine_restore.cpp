#include "test_framework.h"
#include "fakes.h"
#include "core/engine.h"

using namespace macchg;
using fakes::World;

static Mac M(const wchar_t* s) { Mac m; ParseMac(s, m); return m; }
static ChangeRequest ReqFor(const World& w, const wchar_t* mac) {
    ChangeRequest r; r.identity = w.A; r.newMac = M(mac); r.verifyTimeoutMs = 5000; r.pollIntervalMs = 100; r.settleMs = 300; return r;
}
static RestoreRequest RestoreFor(const World& w) {
    RestoreRequest r; r.identity = w.A; r.baseline = *w.store.Original(w.A.interfaceGuid); r.verifyTimeoutMs = 5000; r.pollIntervalMs = 100; r.settleMs = 300; return r;
}

TEST(Restore_RemovesValuesRecreatedByDhcp_RestoresExactInitialState) {
    World w;
    auto before = w.Snapshot();
    Engine e(w.reg, w.dev, w.sys, w.store);
    REQUIRE((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome == (int)Outcome::Success);
    w.reg.SetSz(w.tcpA, L"DhcpIPAddress", L"10.1.1.1");
    w.reg.SetSz(w.tcpA, L"DhcpServer", L"10.1.1.254");
    w.reg.SetDword(w.tcpA, L"EnableDHCP", 1);
    Report r = e.Restore(RestoreFor(w), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::Restored);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, before[w.classA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpB).values, before[w.tcpB].values));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).subkeys[L"SubKeyX"].values, before[w.tcpA].subkeys[L"SubKeyX"].values));
    CHECK(r.observedMac && *r.observedMac == w.permA);
    CHECK(!w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Restore_IdentityMismatch_NothingChanged) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    REQUIRE((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome == (int)Outcome::Success);
    RestoreRequest rr = RestoreFor(w);
    rr.identity = w.B;   // 기준은 A 인데 B 에 적용하려 함
    int writes = w.reg.writeCalls;
    Report r = e.Restore(rr, nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::NothingChanged);
    CHECK_EQ(r.error.code, (uint32_t)APP_E_IDENTITY_MISMATCH);
    CHECK_EQ(w.reg.writeCalls, writes);
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Restore_WriteFailure_RollsBackToPreRestoreState_KeepsBaseline) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    REQUIRE((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome == (int)Outcome::Success);
    auto changed = w.Snapshot();
    w.reg.failWriteNames.insert(L"DHCPINTERFACEOPTIONS");
    Report r = e.Restore(RestoreFor(w), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::FailedRolledBack);
    CHECK_EQ((int)r.failedStep, (int)Step::RestoreRegistry);
    CHECK_EQ(r.error.code, (uint32_t)5);
    // 복구 직전 상태(변경된 상태)로 되돌아감
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, changed[w.tcpA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, changed[w.classA].values));
    CHECK(w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(w.dev.Find(w.A.interfaceGuid)->currentMac == M(L"0A1B2C3D4E5F"));
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));   // 기준 유지 → 다시 시도 가능
}

TEST(Restore_BaselineDisabledButCurrentlyEnabled_EndsDisabled) {
    World w;
    w.dev.Find(w.A.interfaceGuid)->enabled = false;
    Engine e(w.reg, w.dev, w.sys, w.store);
    REQUIRE((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome == (int)Outcome::AppliedAdapterDisabled);
    // 사용자가 그 사이 장치 관리자에서 켰다
    bool rb = false; w.dev.SetEnabled(w.A, true, rb);
    CHECK(w.dev.Find(w.A.interfaceGuid)->currentMac == M(L"0A1B2C3D4E5F"));
    Report r = e.Restore(RestoreFor(w), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::RestoredUnverified);
    CHECK(!w.dev.Find(w.A.interfaceGuid)->enabled);
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
}

TEST(Restore_MacMismatchIsReportedHonestly) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    REQUIRE((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome == (int)Outcome::Success);
    // 장치의 영구 MAC 이 바뀐(다른 장치로 교체된) 비정상 상황을 흉내낸다
    w.dev.Find(w.A.interfaceGuid)->permanentMac = M(L"001122334499");
    Report r = e.Restore(RestoreFor(w), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::RestoredMacMismatch);
    CHECK(r.observedMac && *r.observedMac == M(L"001122334499"));
    CHECK(!w.reg.Has(w.classA, L"NetworkAddress"));
}

TEST(Restore_DisableFails_NothingChanged) {
    World w;
    Engine e(w.reg, w.dev, w.sys, w.store);
    REQUIRE((int)e.Change(ReqFor(w, L"0A1B2C3D4E5F"), nullptr).outcome == (int)Outcome::Success);
    auto changed = w.Snapshot();
    w.dev.Find(w.A.interfaceGuid)->disableFailCount = 1;
    Report r = e.Restore(RestoreFor(w), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::NothingChanged);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, changed[w.tcpA].values));
    CHECK(w.store.HasOriginal(w.A.interfaceGuid));
}

TEST(Restore_TwoAdaptersIndependent) {
    World w;
    auto before = w.Snapshot();
    Engine e(w.reg, w.dev, w.sys, w.store);
    ChangeRequest ra = ReqFor(w, L"0A0000000001");
    ChangeRequest rb = ReqFor(w, L"0A0000000002"); rb.identity = w.B;
    REQUIRE((int)e.Change(ra, nullptr).outcome == (int)Outcome::Success);
    REQUIRE((int)e.Change(rb, nullptr).outcome == (int)Outcome::Success);
    CHECK_EQ(w.store.Count(), (size_t)2);
    // A 만 복구 → B 는 변경 상태 유지
    Report r = e.Restore(RestoreFor(w), nullptr);
    CHECK_EQ((int)r.outcome, (int)Outcome::Restored);
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, before[w.tcpA].values));
    CHECK(w.reg.Has(w.classB, L"NetworkAddress"));
    CHECK(w.dev.Find(w.B.interfaceGuid)->currentMac == M(L"0A0000000002"));
    CHECK_EQ(w.store.Count(), (size_t)1);
    CHECK(w.store.HasOriginal(w.B.interfaceGuid));
}

TEST(SessionStore_DoesNotOverwriteOriginal) {
    SessionBackupStore st;
    AdapterBackup a; a.identity.interfaceGuid = L"{AAAAAAAA-1111-2222-3333-444444444444}"; a.adapterName = L"first";
    AdapterBackup b = a; b.adapterName = L"second";
    CHECK(st.SetOriginalIfAbsent(a));
    CHECK(!st.SetOriginalIfAbsent(b));
    CHECK_EQ(st.Original(L"{aaaaaaaa-1111-2222-3333-444444444444}")->adapterName, std::wstring(L"first"));  // GUID 대소문자 무시
    st.ReplaceOriginal(b);
    CHECK_EQ(st.Original(a.identity.interfaceGuid)->adapterName, std::wstring(L"second"));
    st.ClearOriginal(a.identity.interfaceGuid);
    CHECK_EQ(st.Count(), (size_t)0);
}
