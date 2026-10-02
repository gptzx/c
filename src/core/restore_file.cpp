#include "restore_file.h"
#include <map>
#include "utf.h"

namespace macchg {

static std::string EncName(const std::wstring& w) {
    std::vector<uint16_t> units = ToUtf16Units(w);
    std::vector<uint8_t> bytes;
    for (uint16_t u : units) { bytes.push_back(static_cast<uint8_t>(u & 0xFF)); bytes.push_back(static_cast<uint8_t>(u >> 8)); }
    return HexEncode(bytes);
}

static bool DecName(const std::string& hex, std::wstring& out) {
    std::vector<uint8_t> bytes;
    if (!HexDecode(hex, bytes) || bytes.size() % 2) return false;
    std::vector<uint16_t> units;
    for (size_t i = 0; i < bytes.size(); i += 2) units.push_back(static_cast<uint16_t>(bytes[i] | (bytes[i + 1] << 8)));
    out = FromUtf16Units(units);
    return true;
}

// 주석에 들어가는 이름은 제어 문자를 공백으로 바꿔 줄 구조를 깨지 않게 한다(주석은 불러올 때 무시됨).
static std::string CommentSafe(const std::wstring& w) {
    std::wstring t = w;
    for (wchar_t& c : t) if (c < 0x20 || c == 0x7F) c = L' ';
    return ToUtf8(t);
}

static std::string Line(const std::string& k, const std::string& v) { return k + "=" + v + "\r\n"; }
static std::string LineW(const std::string& k, const std::wstring& v) { return Line(k, ToUtf8(v)); }

std::string SerializeRestoreFile(const RestoreFile& f) {
    const AdapterBackup& b = f.backup;
    std::string s;
    s += "MACCHG-RESTORE " + std::to_string(f.formatVersion) + "\r\n";
    s += "# MacChanger 복구 파일. 수동으로 편집하지 마세요. 불러올 때 PC·어댑터 식별 정보를 검증합니다.\r\n";
    s += Line("schema", std::to_string(AdapterBackup::kSchemaVersion));
    s += LineW("created", f.createdAt);
    s += LineW("app", f.appVersion);
    s += LineW("machine.guid", f.machine.machineGuid);
    s += Line("machine.name", EncName(f.machine.computerName));
    s += LineW("adapter.guid", b.identity.interfaceGuid);
    s += Line("adapter.instance", EncName(b.identity.deviceInstanceId));
    s += LineW("adapter.driverkey", b.identity.driverKeyIndex);
    s += Line("adapter.name", EncName(b.adapterName));
    s += Line("adapter.enabled", b.adapterWasEnabled ? "1" : "0");
    s += Line("adapter.mac", b.macAtBackup ? ToUtf8(FormatPlain(*b.macAtBackup)) : std::string("unknown"));
    s += Line("networkaddress.exists", b.networkAddressExisted ? "1" : "0");
    if (b.networkAddressExisted) {
        s += Line("networkaddress.type", std::to_string(b.networkAddress.type));
        s += Line("networkaddress.data", HexEncode(b.networkAddress.data));
    }
    s += Line("tcpip.keyexisted", b.tcpipKeyExisted ? "1" : "0");
    s += Line("tcpip.count", std::to_string(b.tcpipValues.size()));
    for (size_t i = 0; i < b.tcpipValues.size(); ++i) {
        const RegValue& v = b.tcpipValues[i];
        std::string p = "tcpip." + std::to_string(i) + ".";
        s += "# " + CommentSafe(v.name.empty() ? std::wstring(L"(기본값)") : v.name) + " : " + ToUtf8(RegTypeName(v.type)) + "\r\n";
        s += Line(p + "name", EncName(v.name));
        s += Line(p + "type", std::to_string(v.type));
        s += Line(p + "data", HexEncode(v.data));
    }
    s += "end\r\n";
    return s;
}

static bool ParseU32(const std::string& s, uint32_t& v) {
    if (s.empty() || s.size() > 10) return false;
    uint64_t acc = 0;
    for (char c : s) { if (c < '0' || c > '9') return false; acc = acc * 10 + (c - '0'); if (acc > 0xFFFFFFFFull) return false; }
    v = static_cast<uint32_t>(acc);
    return true;
}

Status ParseRestoreFile(const std::string& bytes, RestoreFile& out, std::wstring& error) {
    error.clear();
    RestoreFile f;
    std::map<std::string, std::string> kv;
    size_t pos = 0;
    bool headerSeen = false, endSeen = false;
    // UTF-8 BOM 허용
    if (bytes.size() >= 3 && static_cast<unsigned char>(bytes[0]) == 0xEF && static_cast<unsigned char>(bytes[1]) == 0xBB && static_cast<unsigned char>(bytes[2]) == 0xBF) pos = 3;
    while (pos <= bytes.size()) {
        size_t nl = bytes.find('\n', pos);
        std::string line = bytes.substr(pos, nl == std::string::npos ? std::string::npos : nl - pos);
        pos = (nl == std::string::npos) ? bytes.size() + 1 : nl + 1;
        line = Trim(line);
        if (line.empty() || line[0] == '#') continue;
        if (!headerSeen) {
            if (line.rfind("MACCHG-RESTORE ", 0) != 0) { error = L"복구 파일 헤더가 아닙니다."; return Status::Fail(APP_E_FILE_FORMAT, L"Header"); }
            uint32_t ver = 0;
            if (!ParseU32(Trim(line.substr(15)), ver)) { error = L"형식 버전을 읽을 수 없습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"Version"); }
            if (ver != RestoreFile::kFormatVersion) { error = L"지원하지 않는 복구 파일 형식 버전입니다: " + std::to_wstring(ver); return Status::Fail(APP_E_FILE_FORMAT, L"VersionUnsupported"); }
            f.formatVersion = ver;
            headerSeen = true;
            continue;
        }
        if (line == "end") { endSeen = true; break; }
        size_t eq = line.find('=');
        if (eq == std::string::npos) { error = L"잘못된 줄: " + FromUtf8(line); return Status::Fail(APP_E_FILE_FORMAT, L"Line"); }
        kv[Trim(line.substr(0, eq))] = Trim(line.substr(eq + 1));
    }
    if (!headerSeen) { error = L"비어 있거나 복구 파일이 아닙니다."; return Status::Fail(APP_E_FILE_FORMAT, L"Empty"); }
    if (!endSeen) { error = L"파일이 끝까지 기록되지 않았습니다(end 누락)."; return Status::Fail(APP_E_FILE_FORMAT, L"NoEnd"); }

    auto need = [&](const char* k, std::string& v) -> bool { auto it = kv.find(k); if (it == kv.end()) { error = L"필수 항목 누락: " + FromUtf8(k); return false; } v = it->second; return true; };
    std::string v;
    uint32_t n = 0;
    if (!need("schema", v) || !ParseU32(v, n)) return Status::Fail(APP_E_FILE_FORMAT, L"schema");
    if (n != AdapterBackup::kSchemaVersion) { error = L"지원하지 않는 백업 스키마 버전입니다: " + std::to_wstring(n); return Status::Fail(APP_E_FILE_FORMAT, L"schemaUnsupported"); }
    if (kv.count("created")) f.createdAt = FromUtf8(kv["created"]);
    if (kv.count("app")) f.appVersion = FromUtf8(kv["app"]);
    if (!need("machine.guid", v)) return Status::Fail(APP_E_FILE_FORMAT, L"machine.guid");
    f.machine.machineGuid = FromUtf8(v);
    if (!need("machine.name", v) || !DecName(v, f.machine.computerName)) return Status::Fail(APP_E_FILE_FORMAT, L"machine.name");
    AdapterBackup& b = f.backup;
    if (!need("adapter.guid", v)) return Status::Fail(APP_E_FILE_FORMAT, L"adapter.guid");
    b.identity.interfaceGuid = FromUtf8(v);
    if (b.identity.interfaceGuid.size() != 38 || b.identity.interfaceGuid.front() != L'{' || b.identity.interfaceGuid.back() != L'}') { error = L"어댑터 GUID 형식이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"adapter.guid"); }
    if (!need("adapter.instance", v) || !DecName(v, b.identity.deviceInstanceId) || b.identity.deviceInstanceId.empty()) { if (error.empty()) error = L"장치 인스턴스 ID 가 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"adapter.instance"); }
    if (!need("adapter.driverkey", v)) return Status::Fail(APP_E_FILE_FORMAT, L"adapter.driverkey");
    b.identity.driverKeyIndex = FromUtf8(v);
    if (b.identity.driverKeyIndex.size() != 4) { error = L"드라이버 키 인덱스 형식이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"adapter.driverkey"); }
    for (wchar_t c : b.identity.driverKeyIndex) if (c < L'0' || c > L'9') { error = L"드라이버 키 인덱스 형식이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"adapter.driverkey"); }
    if (!need("adapter.name", v) || !DecName(v, b.adapterName)) return Status::Fail(APP_E_FILE_FORMAT, L"adapter.name");
    if (!need("adapter.enabled", v)) return Status::Fail(APP_E_FILE_FORMAT, L"adapter.enabled");
    if (v != "0" && v != "1") { error = L"adapter.enabled 값이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"adapter.enabled"); }
    b.adapterWasEnabled = (v == "1");
    if (!need("adapter.mac", v)) return Status::Fail(APP_E_FILE_FORMAT, L"adapter.mac");
    if (v != "unknown") { Mac m; if (!ParseMac(FromUtf8(v), m)) { error = L"adapter.mac 형식이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"adapter.mac"); } b.macAtBackup = m; }
    if (!need("networkaddress.exists", v)) return Status::Fail(APP_E_FILE_FORMAT, L"networkaddress.exists");
    if (v != "0" && v != "1") { error = L"networkaddress.exists 값이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"networkaddress.exists"); }
    b.networkAddressExisted = (v == "1");
    if (b.networkAddressExisted) {
        if (!need("networkaddress.type", v) || !ParseU32(v, b.networkAddress.type)) return Status::Fail(APP_E_FILE_FORMAT, L"networkaddress.type");
        if (!need("networkaddress.data", v) || !HexDecode(v, b.networkAddress.data)) { if (error.empty()) error = L"networkaddress.data 16진수가 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"networkaddress.data"); }
        b.networkAddress.name = L"NetworkAddress";
    }
    if (!need("tcpip.keyexisted", v)) return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.keyexisted");
    if (v != "0" && v != "1") { error = L"tcpip.keyexisted 값이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.keyexisted"); }
    b.tcpipKeyExisted = (v == "1");
    uint32_t count = 0;
    if (!need("tcpip.count", v) || !ParseU32(v, count)) return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.count");
    if (count > 100000) { error = L"tcpip.count 가 비정상적으로 큽니다."; return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.count"); }
    for (uint32_t i = 0; i < count; ++i) {
        std::string p = "tcpip." + std::to_string(i) + ".";
        RegValue rv;
        if (!need((p + "name").c_str(), v) || !DecName(v, rv.name)) { if (error.empty()) error = L"값 이름 인코딩이 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.name"); }
        if (!need((p + "type").c_str(), v) || !ParseU32(v, rv.type)) return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.type");
        if (!need((p + "data").c_str(), v) || !HexDecode(v, rv.data)) { if (error.empty()) error = L"값 데이터 16진수가 잘못되었습니다."; return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.data"); }
        // 같은 이름(대소문자 무시)이 두 번 나오면 손상된 파일
        for (const RegValue& e : b.tcpipValues) if (NameEqualsNoCase(e.name, rv.name)) { error = L"중복된 값 이름: " + rv.name; return Status::Fail(APP_E_FILE_FORMAT, L"tcpip.dup"); }
        b.tcpipValues.push_back(rv);
    }
    out = f;
    return Status::Ok();
}

Status ValidateRestoreFile(const RestoreFile& f, const MachineIdentity& currentMachine, const std::vector<AdapterIdentity>& liveAdapters, std::wstring& error) {
    error.clear();
    if (f.formatVersion != RestoreFile::kFormatVersion) { error = L"형식 버전 불일치"; return Status::Fail(APP_E_FILE_FORMAT, L"Validate/Version"); }
    if (currentMachine.machineGuid.empty() || !NameEqualsNoCase(f.machine.machineGuid, currentMachine.machineGuid)) {
        error = L"이 복구 파일은 다른 PC(" + f.machine.computerName + L")에서 만들어졌습니다. 현재 PC 의 식별 정보와 일치하지 않습니다.";
        return Status::Fail(APP_E_FILE_MACHINE_MISMATCH, L"Validate/Machine");
    }
    const AdapterIdentity& want = f.backup.identity;
    int guidMatches = 0;
    bool full = false;
    for (const AdapterIdentity& a : liveAdapters) {
        if (NameEqualsNoCase(a.interfaceGuid, want.interfaceGuid)) {
            ++guidMatches;
            if (a == want) full = true;
        }
    }
    if (guidMatches == 0) { error = L"복구 파일의 어댑터(" + f.backup.adapterName + L", " + want.interfaceGuid + L")가 현재 PC 에 없습니다."; return Status::Fail(APP_E_FILE_ADAPTER_MISMATCH, L"Validate/AdapterMissing"); }
    if (guidMatches > 1) { error = L"같은 GUID 를 가진 장치가 둘 이상 있어 대상을 확정할 수 없습니다."; return Status::Fail(APP_E_AMBIGUOUS_DEVICE, L"Validate/Ambiguous"); }
    if (!full) { error = L"어댑터 GUID 는 같지만 장치 인스턴스 ID 또는 드라이버 키가 달라졌습니다(드라이버 재설치 등). 안전을 위해 복원하지 않습니다."; return Status::Fail(APP_E_FILE_ADAPTER_MISMATCH, L"Validate/IdentityChanged"); }
    return Status::Ok();
}

} // namespace macchg
