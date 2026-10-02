// win_system.h - 시계/대기, 오류 메시지, 권한/WOW64 확인, PC 식별
#pragma once
#include <windows.h>
#include <string>
#include "core/backend.h"
#include "core/restore_file.h"

namespace macchg {

class WinSystem : public ISystem {
public:
    uint64_t NowMs() override;
    void SleepMs(uint32_t ms) override;
};

std::wstring Win32ErrorMessage(uint32_t code);   // FormatMessageW + SetupAPI 코드 표
bool IsProcessElevated();
// 32비트 프로세스가 64비트 OS 에서 실행 중(SetupDiCallClassInstaller 가 ERROR_IN_WOW64 로 실패)
bool IsRunningUnderWow64();
// 네이티브 머신 아키텍처가 프로세스와 다른지(ARM64 에서의 x64 에뮬레이션 등). 알 수 없으면 false
bool IsRunningUnderEmulation(std::wstring* nativeArch);
MachineIdentity ReadMachineIdentity();
std::wstring LocalTimestampString();
std::wstring ProcessArchLabel();   // "x64" / "x86" / "ARM64"

} // namespace macchg
