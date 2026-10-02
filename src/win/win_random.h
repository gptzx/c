// win_random.h - BCryptGenRandom 기반 IRandomSource (Vista 이상; Windows 7 SP1 포함)
#pragma once
#include "core/backend.h"

namespace macchg {
class WinRandomSource : public IRandomSource {
public:
    bool Fill(uint8_t* buf, size_t len) override;
};
}
