#include "win_registry.h"
#include <vector>

namespace macchg {

const wchar_t* const kNetClassBasePath = L"SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}";
const wchar_t* const kTcpipInterfacesBasePath = L"SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces";

WinRegistryBackend WinRegistryBackend::ForSystem() {
    return WinRegistryBackend(HKEY_LOCAL_MACHINE, kNetClassBasePath, kTcpipInterfacesBasePath, KEY_WOW64_64KEY);
}

WinRegistryBackend::WinRegistryBackend(HKEY root, std::wstring classBase, std::wstring tcpipBase, REGSAM extraSam)
    : root_(root), classBase_(std::move(classBase)), tcpipBase_(std::move(tcpipBase)), extraSam_(extraSam) {}

static bool IsHex(wchar_t c) { return (c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'F') || (c >= L'a' && c <= L'f'); }

bool WinRegistryBackend::IsValidId(const RegKeyId& key) {
    if (key.kind == RegKeyKind::NetClassDriver) {
        if (key.id.size() != 4) return false;
        for (wchar_t c : key.id) if (c < L'0' || c > L'9') return false;
        return true;
    }
    // {8-4-4-4-12}
    const std::wstring& g = key.id;
    if (g.size() != 38 || g[0] != L'{' || g[37] != L'}') return false;
    for (size_t i = 1; i < 37; ++i) {
        if (i == 9 || i == 14 || i == 19 || i == 24) { if (g[i] != L'-') return false; }
        else if (!IsHex(g[i])) return false;
    }
    return true;
}

std::wstring WinRegistryBackend::PathFor(const RegKeyId& key) const {
    return (key.kind == RegKeyKind::NetClassDriver ? classBase_ : tcpipBase_) + L"\\" + key.id;
}

Status WinRegistryBackend::Open(const RegKeyId& key, REGSAM access, HKEY& out) const {
    out = nullptr;
    if (!IsValidId(key)) return Status::Fail(APP_E_IDENTITY_MISMATCH, L"Registry/InvalidKeyId");
    LSTATUS r = RegOpenKeyExW(root_, PathFor(key).c_str(), 0, access | extraSam_, &out);
    if (r != ERROR_SUCCESS) return Status::Fail(static_cast<uint32_t>(r), L"RegOpenKeyExW");
    return Status::Ok();
}

Status WinRegistryBackend::KeyExists(const RegKeyId& key, bool& exists) {
    exists = false;
    HKEY h = nullptr;
    Status s = Open(key, KEY_QUERY_VALUE, h);
    if (s.ok()) { RegCloseKey(h); exists = true; return Status::Ok(); }
    if (s.code == ERROR_FILE_NOT_FOUND) return Status::Ok();
    return s;
}

Status WinRegistryBackend::EnumerateValues(const RegKeyId& key, std::vector<RegValue>& out) {
    out.clear();
    HKEY h = nullptr;
    Status s = Open(key, KEY_QUERY_VALUE, h);
    if (!s.ok()) return s;

    Status result = Status::Ok();
    bool consistent = false;
    for (int attempt = 0; attempt < 4 && !consistent; ++attempt) {
        DWORD cValues = 0, cchMaxName = 0, cbMaxData = 0;
        FILETIME ft1{};
        LSTATUS r = RegQueryInfoKeyW(h, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, &cValues, &cchMaxName, &cbMaxData, nullptr, &ft1);
        if (r != ERROR_SUCCESS) { result = Status::Fail(static_cast<uint32_t>(r), L"RegQueryInfoKeyW"); break; }

        // 값 이름 최대 길이는 16383 문자. 널 포함 16384 로 고정 버퍼를 쓴다.
        std::vector<wchar_t> name(16384);
        std::vector<uint8_t> data(cbMaxData + 64);
        std::vector<RegValue> list;
        result = Status::Ok();
        for (DWORD idx = 0;; ++idx) {
            DWORD cch = static_cast<DWORD>(name.size());
            DWORD type = 0;
            DWORD cb = static_cast<DWORD>(data.size());
            r = RegEnumValueW(h, idx, name.data(), &cch, nullptr, &type, data.data(), &cb);
            if (r == ERROR_NO_MORE_ITEMS) break;
            if (r == ERROR_MORE_DATA) {
                // 열거 도중 값이 커졌다: 버퍼를 늘려 같은 인덱스를 다시 읽는다.
                data.resize(static_cast<size_t>(cb) + 64);
                --idx;
                continue;
            }
            if (r != ERROR_SUCCESS) { result = Status::Fail(static_cast<uint32_t>(r), L"RegEnumValueW"); break; }
            RegValue v;
            v.name.assign(name.data(), cch);
            v.type = type;
            v.data.assign(data.begin(), data.begin() + cb);
            list.push_back(std::move(v));
        }
        if (!result.ok()) break;

        // 열거 중 키가 변하지 않았는지 확인(값 개수·마지막 기록 시각)
        DWORD cValues2 = 0;
        FILETIME ft2{};
        r = RegQueryInfoKeyW(h, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, &cValues2, nullptr, nullptr, nullptr, &ft2);
        if (r != ERROR_SUCCESS) { result = Status::Fail(static_cast<uint32_t>(r), L"RegQueryInfoKeyW"); break; }
        if (cValues2 == cValues && cValues == list.size() && ft1.dwLowDateTime == ft2.dwLowDateTime && ft1.dwHighDateTime == ft2.dwHighDateTime) {
            out = std::move(list);
            consistent = true;
        }
    }
    RegCloseKey(h);
    if (!result.ok()) return result;
    if (!consistent) return Status::Fail(APP_E_BACKUP_INCOMPLETE, L"RegEnumValueW/Unstable");
    return Status::Ok();
}

Status WinRegistryBackend::ReadValue(const RegKeyId& key, const std::wstring& name, bool& found, RegValue& out) {
    found = false;
    HKEY h = nullptr;
    Status s = Open(key, KEY_QUERY_VALUE, h);
    if (!s.ok()) return s;
    std::vector<uint8_t> data(256);
    for (;;) {
        DWORD type = 0;
        DWORD cb = static_cast<DWORD>(data.size());
        LSTATUS r = RegQueryValueExW(h, name.c_str(), nullptr, &type, data.data(), &cb);
        if (r == ERROR_FILE_NOT_FOUND) { RegCloseKey(h); return Status::Ok(); }
        if (r == ERROR_MORE_DATA) { data.resize(static_cast<size_t>(cb) + 64); continue; }
        if (r != ERROR_SUCCESS) { RegCloseKey(h); return Status::Fail(static_cast<uint32_t>(r), L"RegQueryValueExW"); }
        out.name = name;
        out.type = type;
        out.data.assign(data.begin(), data.begin() + cb);
        found = true;
        break;
    }
    RegCloseKey(h);
    return Status::Ok();
}

Status WinRegistryBackend::WriteValue(const RegKeyId& key, const RegValue& value) {
    HKEY h = nullptr;
    Status s = Open(key, KEY_SET_VALUE, h);
    if (!s.ok()) return s;
    LSTATUS r = RegSetValueExW(h, value.name.c_str(), 0, value.type, value.data.empty() ? nullptr : value.data.data(), static_cast<DWORD>(value.data.size()));
    RegCloseKey(h);
    if (r != ERROR_SUCCESS) return Status::Fail(static_cast<uint32_t>(r), L"RegSetValueExW");
    return Status::Ok();
}

Status WinRegistryBackend::DeleteValue(const RegKeyId& key, const std::wstring& name, bool& existed) {
    existed = false;
    HKEY h = nullptr;
    Status s = Open(key, KEY_SET_VALUE, h);
    if (!s.ok()) return s;
    LSTATUS r = RegDeleteValueW(h, name.c_str());   // 빈 이름 = 기본값
    RegCloseKey(h);
    if (r == ERROR_FILE_NOT_FOUND) return Status::Ok();
    if (r != ERROR_SUCCESS) return Status::Fail(static_cast<uint32_t>(r), L"RegDeleteValueW");
    existed = true;
    return Status::Ok();
}

} // namespace macchg
