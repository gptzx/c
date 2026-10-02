// win_registry.h - IRegistryBackend 의 Windows 구현 (RegEnumValueW/RegQueryValueExW/RegSetValueExW/RegDeleteValueW)
#pragma once
#include <windows.h>
#include <string>
#include "core/backend.h"

namespace macchg {

class WinRegistryBackend : public IRegistryBackend {
public:
    // 운영용: HKLM\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-...} 와
    //        HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces 를 64비트 뷰로 연다.
    static WinRegistryBackend ForSystem();
    // 테스트용: 임의 루트/베이스 경로(예: HKCU\Software\MacChangerTest\...). 실제 HKLM 네트워크 키를 건드리지 않는다.
    WinRegistryBackend(HKEY root, std::wstring classBase, std::wstring tcpipBase, REGSAM extraSam);

    Status KeyExists(const RegKeyId& key, bool& exists) override;
    Status EnumerateValues(const RegKeyId& key, std::vector<RegValue>& out) override;
    Status ReadValue(const RegKeyId& key, const std::wstring& name, bool& found, RegValue& out) override;
    Status WriteValue(const RegKeyId& key, const RegValue& value) override;
    Status DeleteValue(const RegKeyId& key, const std::wstring& name, bool& existed) override;

    // 형식화된 식별자가 허용 형식인지(4자리 숫자 / {GUID}). 하위 키로 빠져나가는 경로를 차단한다.
    static bool IsValidId(const RegKeyId& key);
    std::wstring PathFor(const RegKeyId& key) const;

private:
    Status Open(const RegKeyId& key, REGSAM access, HKEY& out) const;
    HKEY root_;
    std::wstring classBase_;
    std::wstring tcpipBase_;
    REGSAM extraSam_;
};

// 두 운영 경로 상수
extern const wchar_t* const kNetClassBasePath;
extern const wchar_t* const kTcpipInterfacesBasePath;

} // namespace macchg
