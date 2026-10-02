#include "mac_address.h"
#include "backend.h"

namespace macchg {

static int HexValW(wchar_t c) {
    if (c >= L'0' && c <= L'9') return c - L'0';
    if (c >= L'a' && c <= L'f') return c - L'a' + 10;
    if (c >= L'A' && c <= L'F') return c - L'A' + 10;
    return -1;
}

bool ParseMac(const std::wstring& text, Mac& out) {
    std::wstring digits;
    size_t b = 0, e = text.size();
    while (b < e && (text[b] == L' ' || text[b] == L'\t')) ++b;
    while (e > b && (text[e - 1] == L' ' || text[e - 1] == L'\t' || text[e - 1] == L'\r' || text[e - 1] == L'\n')) --e;
    for (size_t i = b; i < e; ++i) {
        wchar_t c = text[i];
        if (c == L':' || c == L'-' || c == L'.' || c == L' ') continue;
        if (HexValW(c) < 0) return false;
        digits.push_back(c);
        if (digits.size() > 12) return false;
    }
    if (digits.size() != 12) return false;
    for (int i = 0; i < 6; ++i) {
        out.b[i] = static_cast<uint8_t>((HexValW(digits[i * 2]) << 4) | HexValW(digits[i * 2 + 1]));
    }
    return true;
}

static const wchar_t kHexW[] = L"0123456789ABCDEF";

std::wstring FormatPlain(const Mac& m) {
    std::wstring s;
    s.reserve(12);
    for (int i = 0; i < 6; ++i) { s.push_back(kHexW[m.b[i] >> 4]); s.push_back(kHexW[m.b[i] & 0xF]); }
    return s;
}

std::wstring FormatColon(const Mac& m) {
    std::wstring s;
    s.reserve(17);
    for (int i = 0; i < 6; ++i) {
        if (i) s.push_back(L':');
        s.push_back(kHexW[m.b[i] >> 4]);
        s.push_back(kHexW[m.b[i] & 0xF]);
    }
    return s;
}

bool IsUnicast(const Mac& m) { return (m.b[0] & 0x01) == 0; }
bool IsLocallyAdministered(const Mac& m) { return (m.b[0] & 0x02) != 0; }
bool IsAllZero(const Mac& m) { for (int i = 0; i < 6; ++i) if (m.b[i]) return false; return true; }
bool IsBroadcast(const Mac& m) { for (int i = 0; i < 6; ++i) if (m.b[i] != 0xFF) return false; return true; }

Status ValidateUserMac(const Mac& m) {
    if (!IsUnicast(m)) return Status::Fail(APP_E_INVALID_MAC, L"multicast-bit");
    if (IsAllZero(m)) return Status::Fail(APP_E_INVALID_MAC, L"all-zero");
    if (IsBroadcast(m)) return Status::Fail(APP_E_INVALID_MAC, L"broadcast");
    return Status::Ok();
}

Status GenerateRandomMac(IRandomSource& rng, const RandomMacOptions& opt, const std::vector<Mac>& avoid, Mac& out) {
    int attempts = opt.maxAttempts > 0 ? opt.maxAttempts : 1;
    for (int attempt = 0; attempt < attempts; ++attempt) {
        Mac m;
        if (!rng.Fill(m.b, sizeof(m.b))) {
            return Status::Fail(APP_E_RNG_FAILED, L"IRandomSource::Fill");
        }
        // 첫 바이트: 멀티캐스트 비트(bit0) 해제, 로컬 관리 비트(bit1) 설정
        m.b[0] = static_cast<uint8_t>((m.b[0] & 0xFC) | 0x02);
        if (opt.fixFirstByte02) m.b[0] = 0x02;
        if (IsAllZero(m)) continue;  // 이론상 불가(첫 바이트 >= 0x02)지만 방어적으로 유지
        bool dup = false;
        for (const Mac& a : avoid) if (a == m) { dup = true; break; }
        if (dup) continue;
        out = m;
        return Status::Ok();
    }
    return Status::Fail(APP_E_MAC_COLLISION, L"GenerateRandomMac");
}

} // namespace macchg
