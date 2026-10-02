#include "win_system.h"
#include <cstdio>
#include <cwchar>
#include <vector>

namespace macchg {

uint64_t WinSystem::NowMs() { return GetTickCount64(); }
void WinSystem::SleepMs(uint32_t ms) { Sleep(ms); }

std::wstring Win32ErrorMessage(uint32_t code) {
    // SetupAPI 전용 코드(FormatMessage 가 모름)
    switch (code) {
    case 0xE0000235: return L"ERROR_IN_WOW64: 32비트 프로세스는 64비트 Windows 에서 장치 설치 작업을 수행할 수 없습니다. x64 빌드를 사용하세요.";
    case 0xE0000231: return L"ERROR_NOT_DISABLEABLE: 이 장치는 중지할 수 없습니다.";
    case 0xE000020B: return L"ERROR_NO_SUCH_DEVINST: 장치 인스턴스가 없습니다(분리/제거됨).";
    case 0xE0000203: return L"ERROR_NO_ASSOCIATED_CLASS";
    case 0xE000020C: return L"ERROR_CANT_LOAD_CLASS_ICON";
    case 0xE000020E: return L"ERROR_INVALID_CLASS_INSTALLER";
    case 0xE000020F: return L"ERROR_DI_DO_DEFAULT";
    case 0xE0000217: return L"ERROR_DI_POSTPROCESSING_REQUIRED";
    default: break;
    }
    wchar_t* buf = nullptr;
    DWORD n = FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, nullptr, code, 0, reinterpret_cast<LPWSTR>(&buf), 0, nullptr);
    std::wstring s;
    if (n && buf) {
        s.assign(buf, n);
        LocalFree(buf);
        while (!s.empty() && (s.back() == L'\r' || s.back() == L'\n' || s.back() == L' ')) s.pop_back();
    } else {
        s = L"(메시지 없음)";
    }
    return s;
}

bool IsProcessElevated() {
    HANDLE tok = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &tok)) return false;
    TOKEN_ELEVATION te{};
    DWORD cb = 0;
    bool ok = GetTokenInformation(tok, TokenElevation, &te, sizeof(te), &cb) && te.TokenIsElevated != 0;
    CloseHandle(tok);
    return ok;
}

bool IsRunningUnderWow64() {
    BOOL wow = FALSE;
    typedef BOOL(WINAPI * Fn)(HANDLE, PBOOL);
    HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
    Fn fn = k32 ? reinterpret_cast<Fn>(reinterpret_cast<void*>(GetProcAddress(k32, "IsWow64Process"))) : nullptr;
    if (fn && fn(GetCurrentProcess(), &wow)) return wow != FALSE;
    return false;
}

bool IsRunningUnderEmulation(std::wstring* nativeArch) {
    // IsWow64Process2 (Windows 10 1511+). 없으면 알 수 없음 → false
    typedef BOOL(WINAPI * Fn2)(HANDLE, USHORT*, USHORT*);
    HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
    Fn2 fn = k32 ? reinterpret_cast<Fn2>(reinterpret_cast<void*>(GetProcAddress(k32, "IsWow64Process2"))) : nullptr;
    if (!fn) return false;
    USHORT proc = 0, native = 0;
    if (!fn(GetCurrentProcess(), &proc, &native)) return false;
    if (nativeArch) {
        switch (native) {
        case 0x8664: *nativeArch = L"x64"; break;
        case 0x014c: *nativeArch = L"x86"; break;
        case 0xAA64: *nativeArch = L"ARM64"; break;
        default: *nativeArch = L"0x" + std::to_wstring(native); break;
        }
    }
#if defined(_M_ARM64)
    return false;
#elif defined(_M_X64) || defined(__x86_64__)
    return native != 0x8664;   // x64 프로세스가 ARM64 등에서 에뮬레이션 중
#else
    return proc != 0 /* WOW64 */ && native != 0x014c;
#endif
}

MachineIdentity ReadMachineIdentity() {
    MachineIdentity m;
    HKEY h = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Cryptography", 0, KEY_QUERY_VALUE | KEY_WOW64_64KEY, &h) == ERROR_SUCCESS) {
        wchar_t buf[128] = {};
        DWORD cb = sizeof(buf) - sizeof(wchar_t);
        DWORD type = 0;
        if (RegQueryValueExW(h, L"MachineGuid", nullptr, &type, reinterpret_cast<BYTE*>(buf), &cb) == ERROR_SUCCESS && type == REG_SZ) m.machineGuid = buf;
        RegCloseKey(h);
    }
    wchar_t name[MAX_COMPUTERNAME_LENGTH + 1] = {};
    DWORD n = MAX_COMPUTERNAME_LENGTH + 1;
    if (GetComputerNameW(name, &n)) m.computerName.assign(name, n);
    return m;
}

std::wstring LocalTimestampString() {
    SYSTEMTIME st{};
    GetLocalTime(&st);
    wchar_t buf[64];
    swprintf(buf, 64, L"%04u-%02u-%02u %02u:%02u:%02u", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    return buf;
}

std::wstring ProcessArchLabel() {
#if defined(_M_ARM64) || defined(__aarch64__)
    return L"ARM64";
#elif defined(_M_X64) || defined(__x86_64__)
    return L"x64";
#else
    return L"x86";
#endif
}

} // namespace macchg
