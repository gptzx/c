#include "win_random.h"
#include <windows.h>
#include <bcrypt.h>

namespace macchg {

bool WinRandomSource::Fill(uint8_t* buf, size_t len) {
    if (!buf || len == 0 || len > 0xFFFFFFFFu) return false;
    // 시스템 기본 RNG(CNG). 실패하면 false — 시간값/의사난수 대체 없음.
    NTSTATUS st = BCryptGenRandom(nullptr, buf, static_cast<ULONG>(len), BCRYPT_USE_SYSTEM_PREFERRED_RNG);
    return st >= 0;  // NT_SUCCESS
}

} // namespace macchg
