// 최소 테스트 프레임워크(외부 의존성 없음)
#pragma once
#include <cstdio>
#include <functional>
#include <string>
#include <vector>

namespace tf {

struct TestCase { const char* name; std::function<void()> fn; };
inline std::vector<TestCase>& Registry() { static std::vector<TestCase> r; return r; }
struct Registrar { Registrar(const char* n, std::function<void()> f) { Registry().push_back({n, std::move(f)}); } };

struct Failure { std::string msg; };
inline int& FailCount() { static int c = 0; return c; }
inline const char*& CurrentTest() { static const char* t = ""; return t; }

inline void Fail(const char* file, int line, const std::string& expr) {
    ++FailCount();
    std::fprintf(stderr, "  FAIL %s:%d [%s] %s\n", file, line, CurrentTest(), expr.c_str());
}

inline std::string W2A(const std::wstring& w) { std::string s; for (wchar_t c : w) s.push_back(c < 128 ? static_cast<char>(c) : '?'); return s; }

} // namespace tf

#define TEST(name) static void name(); static tf::Registrar name##_reg(#name, name); static void name()
#define CHECK(expr) do { if (!(expr)) tf::Fail(__FILE__, __LINE__, #expr); } while (0)
#define CHECK_EQ(a, b) do { if (!((a) == (b))) tf::Fail(__FILE__, __LINE__, std::string(#a " == " #b)); } while (0)
#define REQUIRE(expr) do { if (!(expr)) { tf::Fail(__FILE__, __LINE__, std::string("REQUIRE ") + #expr); return; } } while (0)
