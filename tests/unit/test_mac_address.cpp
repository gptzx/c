#include "test_framework.h"
#include "fakes.h"
#include "core/mac_address.h"

using namespace macchg;

TEST(ParseMac_AcceptsCommonFormats) {
    Mac m;
    REQUIRE(ParseMac(L"0A1B2C3D4E5F", m));
    CHECK_EQ(FormatPlain(m), std::wstring(L"0A1B2C3D4E5F"));
    REQUIRE(ParseMac(L"0a:1b:2c:3d:4e:5f", m));
    CHECK_EQ(FormatColon(m), std::wstring(L"0A:1B:2C:3D:4E:5F"));
    REQUIRE(ParseMac(L" 0A-1B-2C-3D-4E-5F \r\n", m));
    REQUIRE(ParseMac(L"0a1b.2c3d.4e5f", m));
    CHECK_EQ(FormatPlain(m), std::wstring(L"0A1B2C3D4E5F"));
}

TEST(ParseMac_RejectsInvalid) {
    Mac m;
    CHECK(!ParseMac(L"", m));
    CHECK(!ParseMac(L"0A1B2C3D4E5", m));      // 11자리
    CHECK(!ParseMac(L"0A1B2C3D4E5F00", m));   // 14자리
    CHECK(!ParseMac(L"0G1B2C3D4E5F", m));     // 비16진
    CHECK(!ParseMac(L"0A:1B:2C:3D:4E", m));   // 5옥텟
}

TEST(ValidateUserMac_RejectsMulticastZeroBroadcast) {
    Mac m;
    ParseMac(L"010000000001", m);
    CHECK_EQ(ValidateUserMac(m).code, (uint32_t)APP_E_INVALID_MAC);
    ParseMac(L"000000000000", m);
    CHECK_EQ(ValidateUserMac(m).code, (uint32_t)APP_E_INVALID_MAC);
    ParseMac(L"FFFFFFFFFFFF", m);
    CHECK_EQ(ValidateUserMac(m).code, (uint32_t)APP_E_INVALID_MAC);
    ParseMac(L"001122334455", m);   // 제조사 OUI 형태(로컬 관리 비트 꺼짐)도 허용
    CHECK(ValidateUserMac(m).ok());
    ParseMac(L"0A1122334455", m);
    CHECK(ValidateUserMac(m).ok());
}

TEST(RandomMac_LengthCharsAndBits) {
    fakes::FakeRandom rng;
    RandomMacOptions opt;
    int seen[4] = {0, 0, 0, 0};
    for (int i = 0; i < 2000; ++i) {
        Mac m;
        REQUIRE(GenerateRandomMac(rng, opt, {}, m).ok());
        std::wstring s = FormatPlain(m);
        CHECK_EQ(s.size(), (size_t)12);
        for (wchar_t c : s) CHECK((c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'F'));
        // 왼쪽에서 두 번째 16진 문자는 2/6/A/E 중 하나
        wchar_t second = s[1];
        CHECK(second == L'2' || second == L'6' || second == L'A' || second == L'E');
        if (second == L'2') ++seen[0]; else if (second == L'6') ++seen[1]; else if (second == L'A') ++seen[2]; else ++seen[3];
        CHECK(IsUnicast(m));
        CHECK(IsLocallyAdministered(m));
        CHECK_EQ((int)(m.b[0] & 0x03), 0x02);
    }
    // 네 값이 모두 나타나야 한다(첫 바이트 상위 비트가 고정되지 않음)
    CHECK(seen[0] > 0 && seen[1] > 0 && seen[2] > 0 && seen[3] > 0);
}

TEST(RandomMac_FixFirstByte02) {
    fakes::FakeRandom rng;
    RandomMacOptions opt;
    opt.fixFirstByte02 = true;
    for (int i = 0; i < 500; ++i) {
        Mac m;
        REQUIRE(GenerateRandomMac(rng, opt, {}, m).ok());
        CHECK_EQ((int)m.b[0], 0x02);
        CHECK(IsUnicast(m) && IsLocallyAdministered(m));
    }
}

TEST(RandomMac_AvoidsCollisionsWithOtherAdapters) {
    fakes::FakeRandom rng;
    // 스크립트: 처음 6바이트는 avoid 와 같은 값 → 재시도 → 두 번째 6바이트는 다름
    rng.scripted = {0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x00, 0x11, 0x22, 0x33, 0x44, 0x66};
    Mac avoid1; avoid1.b[0] = 0x02; avoid1.b[1] = 0x11; avoid1.b[2] = 0x22; avoid1.b[3] = 0x33; avoid1.b[4] = 0x44; avoid1.b[5] = 0x55;  // 첫 바이트 연산 적용 후 값
    Mac m;
    RandomMacOptions opt;
    REQUIRE(GenerateRandomMac(rng, opt, {avoid1}, m).ok());
    CHECK(m != avoid1);
    CHECK_EQ(FormatPlain(m), std::wstring(L"021122334466"));
}

TEST(RandomMac_CollisionExhaustionFails) {
    fakes::FakeRandom rng;
    rng.scripted = {0x00, 0x11, 0x22, 0x33, 0x44, 0x55};  // 항상 같은 값
    Mac avoid; ParseMac(L"021122334455", avoid);
    Mac m;
    RandomMacOptions opt; opt.maxAttempts = 8;
    Status s = GenerateRandomMac(rng, opt, {avoid}, m);
    CHECK_EQ(s.code, (uint32_t)APP_E_MAC_COLLISION);
}

TEST(RandomMac_RngFailureIsErrorNotFallback) {
    fakes::FakeRandom rng;
    rng.fail = true;
    Mac m; m.b[5] = 0x99;
    Status s = GenerateRandomMac(rng, RandomMacOptions{}, {}, m);
    CHECK_EQ(s.code, (uint32_t)APP_E_RNG_FAILED);
    CHECK_EQ((int)m.b[5], 0x99);  // 출력이 바뀌지 않음
}
