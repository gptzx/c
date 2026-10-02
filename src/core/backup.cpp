#include "backup.h"
#include <cwctype>

namespace macchg {

static bool SameSnapshot(const std::vector<RegValue>& a, const std::vector<RegValue>& b) {
    if (a.size() != b.size()) return false;
    // 순서가 달라질 수 있으므로 이름 기준으로 대응시킨다.
    for (const RegValue& x : a) {
        bool found = false;
        for (const RegValue& y : b) {
            if (NameEqualsNoCase(x.name, y.name)) { if (x.type != y.type || x.data != y.data) return false; found = true; break; }
        }
        if (!found) return false;
    }
    return true;
}

Status CaptureBackup(IRegistryBackend& reg, IAdapterControl& dev, const AdapterIdentity& id, const std::wstring& displayName, AdapterBackup& out) {
    AdapterBackup b;
    b.identity = id;
    b.adapterName = displayName;

    // 1) 장치 상태(활성화 여부, 현재 MAC)
    AdapterState st;
    Status s = dev.QueryState(id, st);
    if (!s.ok()) return s;
    if (!st.present) return Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"CaptureBackup/QueryState");
    b.adapterWasEnabled = st.enabled;
    b.macAtBackup = st.currentMac;

    // 2) NetworkAddress (드라이버 클래스 키)
    const RegKeyId classKey = NetClassDriverKey(id.driverKeyIndex);
    bool classExists = false;
    s = reg.KeyExists(classKey, classExists);
    if (!s.ok()) return s;
    if (!classExists) return Status::Fail(APP_E_IDENTITY_MISMATCH, L"CaptureBackup/ClassKeyMissing");
    bool found = false;
    RegValue na;
    s = reg.ReadValue(classKey, L"NetworkAddress", found, na);
    if (!s.ok()) return s;
    b.networkAddressExisted = found;
    if (found) b.networkAddress = na;

    // 3) TCP/IP 인터페이스 키의 모든 직접 값 — 스냅샷 2회 일치 확인
    const RegKeyId tcpKey = TcpipInterfaceKey(id.interfaceGuid);
    bool tcpExists = false;
    s = reg.KeyExists(tcpKey, tcpExists);
    if (!s.ok()) return s;
    b.tcpipKeyExisted = tcpExists;
    if (tcpExists) {
        std::vector<RegValue> snap1, snap2;
        bool consistent = false;
        for (int attempt = 0; attempt < 3 && !consistent; ++attempt) {
            s = reg.EnumerateValues(tcpKey, snap1);
            if (!s.ok()) return s;
            s = reg.EnumerateValues(tcpKey, snap2);
            if (!s.ok()) return s;
            consistent = SameSnapshot(snap1, snap2);
        }
        if (!consistent) return Status::Fail(APP_E_BACKUP_INCOMPLETE, L"CaptureBackup/TcpipSnapshotUnstable");
        b.tcpipValues = snap1;
    }

    out = b;
    return Status::Ok();
}

std::wstring SessionBackupStore::Key(const std::wstring& guid) {
    std::wstring k = guid;
    for (wchar_t& c : k) c = static_cast<wchar_t>(std::towupper(static_cast<wint_t>(c)));
    return k;
}

bool SessionBackupStore::HasOriginal(const std::wstring& guid) const { return originals_.count(Key(guid)) != 0; }

const AdapterBackup* SessionBackupStore::Original(const std::wstring& guid) const {
    auto it = originals_.find(Key(guid));
    return it == originals_.end() ? nullptr : &it->second;
}

bool SessionBackupStore::SetOriginalIfAbsent(const AdapterBackup& b) {
    std::wstring k = Key(b.identity.interfaceGuid);
    if (originals_.count(k)) return false;
    originals_[k] = b;
    return true;
}

void SessionBackupStore::ReplaceOriginal(const AdapterBackup& b) { originals_[Key(b.identity.interfaceGuid)] = b; }

void SessionBackupStore::ClearOriginal(const std::wstring& guid) { originals_.erase(Key(guid)); }

std::vector<std::wstring> SessionBackupStore::Guids() const {
    std::vector<std::wstring> v;
    for (const auto& kv : originals_) v.push_back(kv.second.identity.interfaceGuid);
    return v;
}

} // namespace macchg
