// utf.h - wchar_t 폭에 독립적인 문자열/16진수 변환 도우미
#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace macchg {

// wstring -> UTF-16 코드 단위 (Windows: 그대로 복사, Linux: 서러게이트 쌍 생성)
std::vector<uint16_t> ToUtf16Units(const std::wstring& s);
std::wstring FromUtf16Units(const std::vector<uint16_t>& units);

std::string ToUtf8(const std::wstring& s);
std::wstring FromUtf8(const std::string& s);  // 잘못된 바이트는 U+FFFD로 치환

std::string HexEncode(const std::vector<uint8_t>& data);     // 대문자, 구분자 없음
std::wstring HexEncodeW(const std::vector<uint8_t>& data);
bool HexDecode(const std::string& hex, std::vector<uint8_t>& out);  // 홀수 길이/비16진 문자 → false

std::wstring TrimW(const std::wstring& s);
std::string Trim(const std::string& s);

} // namespace macchg
