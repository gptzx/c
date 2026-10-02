// mac_address.h - MAC 주소 파싱/서식/검증/랜덤 생성 (플랫폼 독립)
#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include "status.h"

namespace macchg {

struct Mac {
    uint8_t b[6] = {0, 0, 0, 0, 0, 0};
    bool operator==(const Mac& o) const { for (int i = 0; i < 6; ++i) if (b[i] != o.b[i]) return false; return true; }
    bool operator!=(const Mac& o) const { return !(*this == o); }
};

class IRandomSource;

// "0A1B2C3D4E5F", "0A:1B:2C:3D:4E:5F", "0A-1B-...", "0a1b.2c3d.4e5f" 등을 허용. 앞뒤 공백 무시.
bool ParseMac(const std::wstring& text, Mac& out);
std::wstring FormatPlain(const Mac& m);   // 구분자 없는 대문자 12자리
std::wstring FormatColon(const Mac& m);   // 콜론 구분 대문자

bool IsUnicast(const Mac& m);               // 첫 바이트 bit0 == 0
bool IsLocallyAdministered(const Mac& m);   // 첫 바이트 bit1 == 1
bool IsAllZero(const Mac& m);
bool IsBroadcast(const Mac& m);

// 사용자가 직접 입력한 MAC의 허용 여부: 유니캐스트여야 하고 00..00 / FF..FF 는 거부.
// 로컬 관리 비트가 꺼진(제조사 OUI 형태) 주소는 허용하되 UI에서 경고할 수 있도록 별도 함수 제공.
Status ValidateUserMac(const Mac& m);

struct RandomMacOptions {
    bool fixFirstByte02 = false;  // 무선랜 호환 옵션: 첫 바이트를 0x02로 고정
    int maxAttempts = 32;         // 중복 회피 재시도 횟수
};

// 운영체제의 암호학적 난수(IRandomSource)로 6바이트를 생성한 뒤
//   mac[0] = (mac[0] & 0xFC) | 0x02
// 를 적용한다(유니캐스트 + 로컬 관리). fixFirstByte02이면 mac[0] = 0x02.
// avoid 목록(현재 MAC, 같은 PC의 다른 어댑터 MAC)과 겹치면 다시 생성한다.
// 난수 생성 실패 시 시간값/의사난수로 대체하지 않고 APP_E_RNG_FAILED를 반환한다.
Status GenerateRandomMac(IRandomSource& rng, const RandomMacOptions& opt, const std::vector<Mac>& avoid, Mac& out);

} // namespace macchg
