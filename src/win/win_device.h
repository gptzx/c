// win_device.h - IAdapterControl 의 Windows 구현 (SetupAPI DIF_PROPERTYCHANGE DICS_DISABLE/DICS_ENABLE)
#pragma once
#include "core/backend.h"

namespace macchg {

class WinAdapterControl : public IAdapterControl {
public:
    Status VerifyIdentity(const AdapterIdentity& id) override;
    Status QueryState(const AdapterIdentity& id, AdapterState& out) override;
    Status SetEnabled(const AdapterIdentity& id, bool enable, bool& rebootRequired) override;

    uint32_t stateChangeTimeoutMs = 8000;   // 중지/활성화 후 상태 도달 대기
};

} // namespace macchg
