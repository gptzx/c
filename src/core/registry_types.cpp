#include "registry_types.h"
#include "utf.h"

namespace macchg {

std::vector<uint8_t> EncodeSz(const std::wstring& s) {
    std::vector<uint16_t> units = ToUtf16Units(s);
    std::vector<uint8_t> out;
    out.reserve((units.size() + 1) * 2);
    for (uint16_t u : units) {
        out.push_back(static_cast<uint8_t>(u & 0xFF));
        out.push_back(static_cast<uint8_t>(u >> 8));
    }
    out.push_back(0);
    out.push_back(0);
    return out;
}

std::wstring DecodeSz(const std::vector<uint8_t>& data) {
    std::vector<uint16_t> units;
    units.reserve(data.size() / 2);
    for (size_t i = 0; i + 1 < data.size(); i += 2) {
        uint16_t u = static_cast<uint16_t>(data[i] | (data[i + 1] << 8));
        if (u == 0) break;  // 널 종결 (없을 수도 있음)
        units.push_back(u);
    }
    return FromUtf16Units(units);
}

std::vector<uint8_t> EncodeDword(uint32_t v) {
    return {static_cast<uint8_t>(v & 0xFF), static_cast<uint8_t>((v >> 8) & 0xFF),
            static_cast<uint8_t>((v >> 16) & 0xFF), static_cast<uint8_t>((v >> 24) & 0xFF)};
}

bool DecodeDword(const std::vector<uint8_t>& data, uint32_t& v) {
    if (data.size() != 4) return false;
    v = static_cast<uint32_t>(data[0]) | (static_cast<uint32_t>(data[1]) << 8) |
        (static_cast<uint32_t>(data[2]) << 16) | (static_cast<uint32_t>(data[3]) << 24);
    return true;
}

std::wstring RegTypeName(uint32_t type) {
    switch (type) {
    case RT_NONE: return L"REG_NONE";
    case RT_SZ: return L"REG_SZ";
    case RT_EXPAND_SZ: return L"REG_EXPAND_SZ";
    case RT_BINARY: return L"REG_BINARY";
    case RT_DWORD: return L"REG_DWORD";
    case RT_DWORD_BIG_ENDIAN: return L"REG_DWORD_BIG_ENDIAN";
    case RT_LINK: return L"REG_LINK";
    case RT_MULTI_SZ: return L"REG_MULTI_SZ";
    case RT_QWORD: return L"REG_QWORD";
    default: return L"REG_TYPE_" + std::to_wstring(type);
    }
}

std::wstring SummarizeRegData(const RegValue& v, size_t maxChars) {
    std::wstring s;
    switch (v.type) {
    case RT_SZ:
    case RT_EXPAND_SZ:
        s = L"\"" + DecodeSz(v.data) + L"\"";
        break;
    case RT_MULTI_SZ: {
        // 널로 구분된 문자열들을 ", "로 연결
        std::vector<uint16_t> units;
        for (size_t i = 0; i + 1 < v.data.size(); i += 2) units.push_back(static_cast<uint16_t>(v.data[i] | (v.data[i + 1] << 8)));
        std::wstring cur;
        bool first = true;
        std::vector<uint16_t> buf;
        for (size_t i = 0; i < units.size(); ++i) {
            if (units[i] == 0) {
                if (buf.empty()) break;  // 이중 널 = 끝
                if (!first) s += L", ";
                s += L"\"" + FromUtf16Units(buf) + L"\"";
                first = false;
                buf.clear();
            } else {
                buf.push_back(units[i]);
            }
        }
        if (!buf.empty()) { if (!first) s += L", "; s += L"\"" + FromUtf16Units(buf) + L"\""; }
        break;
    }
    case RT_DWORD: {
        uint32_t d = 0;
        if (DecodeDword(v.data, d)) s = std::to_wstring(d);
        else s = L"0x" + HexEncodeW(v.data);
        break;
    }
    default:
        s = L"0x" + HexEncodeW(v.data);
        break;
    }
    if (s.size() > maxChars) s = s.substr(0, maxChars) + L"…";
    return s;
}

} // namespace macchg
