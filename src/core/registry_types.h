// registry_types.h - 레지스트리 값 모델과 "형식화된" 키 식별자 (플랫폼 독립)
//
// 설계 원칙: 코어는 임의의 레지스트리 경로를 다루지 않는다. 쓰기 가능한 키는
// 다음 두 종류의 형식화된 식별자로만 지정된다.
//   NetClassDriver  : HKLM\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}\####
//   TcpipInterface  : HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}
// 실제 경로 조합은 Windows 백엔드가 담당하고, 하위 키를 가리킬 방법은 의도적으로 제공하지 않는다.
#pragma once
#include <cstdint>
#include <cwctype>
#include <string>
#include <vector>

namespace macchg {

enum class RegKeyKind {
    NetClassDriver,   // id = "0001" 같은 4자리 드라이버 키 인덱스
    TcpipInterface,   // id = "{GUID}"
};

struct RegKeyId {
    RegKeyKind kind = RegKeyKind::TcpipInterface;
    std::wstring id;

    bool operator==(const RegKeyId& o) const { return kind == o.kind && id == o.id; }
    bool operator!=(const RegKeyId& o) const { return !(*this == o); }
    bool operator<(const RegKeyId& o) const {
        if (kind != o.kind) return static_cast<int>(kind) < static_cast<int>(o.kind);
        return id < o.id;
    }
};

inline RegKeyId NetClassDriverKey(const std::wstring& driverIndex) { return RegKeyId{RegKeyKind::NetClassDriver, driverIndex}; }
inline RegKeyId TcpipInterfaceKey(const std::wstring& guid) { return RegKeyId{RegKeyKind::TcpipInterface, guid}; }

// winnt.h의 REG_* 와 같은 값. 코어가 Windows 헤더에 의존하지 않도록 재정의.
enum RegType : uint32_t {
    RT_NONE = 0,
    RT_SZ = 1,
    RT_EXPAND_SZ = 2,
    RT_BINARY = 3,
    RT_DWORD = 4,
    RT_DWORD_BIG_ENDIAN = 5,
    RT_LINK = 6,
    RT_MULTI_SZ = 7,
    RT_QWORD = 11,
};

// 레지스트리 값: 이름(기본값은 빈 문자열), 자료형, 원시 바이트.
// 원시 바이트를 그대로 보존하므로 어떤 자료형이든 손실 없이 백업/복원할 수 있다.
struct RegValue {
    std::wstring name;
    uint32_t type = RT_NONE;
    std::vector<uint8_t> data;

    bool operator==(const RegValue& o) const { return name == o.name && type == o.type && data == o.data; }
    bool operator!=(const RegValue& o) const { return !(*this == o); }
};

// 레지스트리 값 이름 비교는 대소문자를 구분하지 않는다.
inline bool NameEqualsNoCase(const std::wstring& a, const std::wstring& b) {
    if (a.size() != b.size()) return false;
    for (size_t i = 0; i < a.size(); ++i) {
        if (std::towupper(static_cast<wint_t>(a[i])) != std::towupper(static_cast<wint_t>(b[i]))) return false;
    }
    return true;
}

// REG_SZ 값을 만든다(널 종결 포함, UTF-16LE 바이트열).
// 코어는 wchar_t 폭에 독립적이어야 하므로 16비트 코드 단위로 직접 직렬화한다.
std::vector<uint8_t> EncodeSz(const std::wstring& s);
// REG_SZ/REG_EXPAND_SZ 원시 바이트를 문자열로 해석(널 종결이 없을 수도 있음을 고려).
std::wstring DecodeSz(const std::vector<uint8_t>& data);
std::vector<uint8_t> EncodeDword(uint32_t v);
bool DecodeDword(const std::vector<uint8_t>& data, uint32_t& v);

inline RegValue MakeSzValue(const std::wstring& name, const std::wstring& s) { return RegValue{name, RT_SZ, EncodeSz(s)}; }
inline RegValue MakeDwordValue(const std::wstring& name, uint32_t v) { return RegValue{name, RT_DWORD, EncodeDword(v)}; }

std::wstring RegTypeName(uint32_t type);
// 사람이 읽을 수 있는 짧은 데이터 요약(표시용).
std::wstring SummarizeRegData(const RegValue& v, size_t maxChars = 48);

} // namespace macchg
