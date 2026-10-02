#include "test_framework.h"
#include "fakes.h"
#include "core/cleanup.h"

using namespace macchg;
using fakes::World;

TEST(Plan_ExcludesEnableDhcpCaseInsensitive_IncludesDefaultValue) {
    std::vector<RegValue> snap = {
        MakeDwordValue(L"enabledhcp", 1),
        MakeSzValue(L"IPAddress", L"1.2.3.4"),
        RegValue{L"", RT_SZ, EncodeSz(L"default")},
        MakeSzValue(L"NameServer", L"8.8.8.8"),
    };
    CleanupPlan p = PlanTcpipCleanup(snap);
    CHECK(p.enableDhcpPresent);
    CHECK_EQ(p.enableDhcp.name, std::wstring(L"enabledhcp"));   // 원래 표기 유지
    CHECK_EQ(p.toDelete.size(), (size_t)3);
    bool hasDefault = false;
    for (auto& v : p.toDelete) { CHECK(!NameEqualsNoCase(v.name, L"EnableDHCP")); if (v.name.empty()) hasDefault = true; }
    CHECK(hasDefault);

    std::vector<RegValue> snap2 = { MakeDwordValue(L"ENABLEDHCP", 0), MakeSzValue(L"X", L"") };
    CleanupPlan p2 = PlanTcpipCleanup(snap2);
    CHECK(p2.enableDhcpPresent);
    CHECK_EQ(p2.toDelete.size(), (size_t)1);
}

TEST(Plan_NoEnableDhcp) {
    std::vector<RegValue> snap = { MakeSzValue(L"IPAddress", L"1.2.3.4") };
    CleanupPlan p = PlanTcpipCleanup(snap);
    CHECK(!p.enableDhcpPresent);
    CHECK_EQ(p.toDelete.size(), (size_t)1);
}

TEST(Execute_DeletesAllButEnableDhcp_PreservesTypeAndData_LeavesSubkeysAndOthers) {
    World w;
    auto before = w.Snapshot();
    const RegValue dhcpBefore = *w.reg.Get(w.tcpA, L"EnableDHCP");
    std::vector<RegValue> snap;
    REQUIRE(w.reg.EnumerateValues(w.tcpA, snap).ok());
    CHECK_EQ(snap.size(), (size_t)13);  // 12개 + 기본값
    CleanupPlan plan = PlanTcpipCleanup(snap);
    CHECK_EQ(plan.toDelete.size(), (size_t)12);
    std::vector<std::wstring> remaining; bool intact = false;
    Status s = ExecuteTcpipCleanup(w.reg, w.tcpA, plan, remaining, intact);
    CHECK(s.ok());
    CHECK(intact);
    CHECK(remaining.empty());
    auto after = w.reg.Values(w.tcpA);
    REQUIRE(after.size() == 1);
    CHECK(NameEqualsNoCase(after[0].name, L"EnableDHCP"));
    CHECK(after[0] == dhcpBefore);   // 자료형·원시 데이터 동일
    // 하위 키, 다른 어댑터, 클래스 키 불변
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).subkeys[L"SubKeyX"].values, before[w.tcpA].subkeys[L"SubKeyX"].values));
    CHECK(fakes::SameValues(w.reg.Key(w.tcpB).values, before[w.tcpB].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classA).values, before[w.classA].values));
    CHECK(fakes::SameValues(w.reg.Key(w.classB).values, before[w.classB].values));
}

TEST(Execute_DoesNotCreateEnableDhcpWhenAbsent) {
    World w;
    bool existed = false;
    REQUIRE(w.reg.DeleteValue(w.tcpA, L"EnableDHCP", existed).ok() && existed);
    std::vector<RegValue> snap;
    REQUIRE(w.reg.EnumerateValues(w.tcpA, snap).ok());
    CleanupPlan plan = PlanTcpipCleanup(snap);
    CHECK(!plan.enableDhcpPresent);
    std::vector<std::wstring> remaining; bool intact = false;
    CHECK(ExecuteTcpipCleanup(w.reg, w.tcpA, plan, remaining, intact).ok());
    CHECK(w.reg.Values(w.tcpA).empty());
    CHECK(!w.reg.Has(w.tcpA, L"EnableDHCP"));
}

TEST(Execute_PartialFailureIsNotSuccess) {
    World w;
    w.reg.failDeleteNames.insert(L"LEASE");
    std::vector<RegValue> snap;
    REQUIRE(w.reg.EnumerateValues(w.tcpA, snap).ok());
    CleanupPlan plan = PlanTcpipCleanup(snap);
    std::vector<std::wstring> remaining; bool intact = false;
    Status s = ExecuteTcpipCleanup(w.reg, w.tcpA, plan, remaining, intact);
    CHECK(!s.ok());
    CHECK_EQ(s.code, (uint32_t)5);   // 실제 삭제 오류 코드가 우선 보고됨
    REQUIRE(remaining.size() == 1);
    CHECK(NameEqualsNoCase(remaining[0], L"Lease"));
}

TEST(Verify_DetectsValueRecreatedAfterPlan) {
    World w;
    std::vector<RegValue> snap;
    REQUIRE(w.reg.EnumerateValues(w.tcpA, snap).ok());
    CleanupPlan plan = PlanTcpipCleanup(snap);
    // 계획 수립 뒤 다른 프로세스가 값을 추가한 상황
    w.reg.SetSz(w.tcpA, L"DhcpServer", L"192.168.0.1");
    std::vector<std::wstring> remaining; bool intact = false;
    Status s = ExecuteTcpipCleanup(w.reg, w.tcpA, plan, remaining, intact);
    CHECK_EQ(s.code, (uint32_t)APP_E_CLEANUP_INCOMPLETE);
    REQUIRE(remaining.size() == 1);
    CHECK_EQ(remaining[0], std::wstring(L"DhcpServer"));
}

TEST(RestoreSnapshot_RestoresAllTypesLongValuesAndDefault_RemovesExtras) {
    World w;
    std::vector<RegValue> snap;
    REQUIRE(w.reg.EnumerateValues(w.tcpA, snap).ok());
    auto originalValues = w.reg.Key(w.tcpA).values;
    // 삭제 후 다른 값 추가
    w.reg.Key(w.tcpA).values.clear();
    w.reg.SetSz(w.tcpA, L"Extra", L"x");
    std::vector<std::wstring> mism;
    Status s = RestoreValueSnapshot(w.reg, w.tcpA, snap, mism);
    CHECK(s.ok());
    CHECK(mism.empty());
    CHECK(fakes::SameValues(w.reg.Key(w.tcpA).values, originalValues));
    CHECK(!w.reg.Has(w.tcpA, L"Extra"));
    // 긴 값(5000바이트)·기본값·QWORD·BIG_ENDIAN 모두 바이트 동일
    CHECK(*w.reg.Get(w.tcpA, L"DhcpInterfaceOptions") == originalValues[L"DHCPINTERFACEOPTIONS"]);
    CHECK(*w.reg.Get(w.tcpA, L"") == originalValues[L""]);
    CHECK(*w.reg.Get(w.tcpA, L"SomeQword") == originalValues[L"SOMEQWORD"]);
}

TEST(RestoreSnapshot_WriteFailureReported) {
    World w;
    std::vector<RegValue> snap;
    REQUIRE(w.reg.EnumerateValues(w.tcpA, snap).ok());
    w.reg.Key(w.tcpA).values.clear();
    w.reg.failWriteNames.insert(L"T1");
    std::vector<std::wstring> mism;
    Status s = RestoreValueSnapshot(w.reg, w.tcpA, snap, mism);
    CHECK(!s.ok());
    REQUIRE(mism.size() == 1);
    CHECK_EQ(mism[0], std::wstring(L"T1"));
}

TEST(DetectStaticSettings_ReportsStaticIpAndDns_IgnoresPlaceholders) {
    World w;
    std::vector<RegValue> a; w.reg.EnumerateValues(w.tcpA, a);
    StaticSettingsSummary sa = DetectStaticSettings(a);
    CHECK(!sa.dhcpDisabled);
    CHECK(sa.items.empty());   // 0.0.0.0 / 빈 NameServer 는 고정 설정이 아님
    std::vector<RegValue> b; w.reg.EnumerateValues(w.tcpB, b);
    StaticSettingsSummary sb = DetectStaticSettings(b);
    CHECK(sb.dhcpDisabled);
    CHECK_EQ(sb.items.size(), (size_t)2);
    CHECK(sb.HasAny());
}
