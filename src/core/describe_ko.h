// describe_ko.h - 결과/오류의 한국어 설명 (표시 전용. 테스트는 코드만 검사한다)
#pragma once
#include <string>
#include "engine.h"

namespace macchg {

std::wstring DescribeOutcome(Outcome o);                   // 한 줄 요약
std::wstring DescribeStep(Step s);
std::wstring DescribeError(const Status& s);                // "오류 코드 5 (액세스가 거부되었습니다) @ RegSetValueExW" 형식. Win32 메시지는 플랫폼 계층에서 공급
std::wstring DescribeAppError(uint32_t code);              // 앱 정의 코드 설명. 모르면 빈 문자열
std::wstring DescribePhase(Phase p);                       // "백업 중" 등 짧은 상태
// 결과 보고 전체(여러 줄). win32Message 는 Win32 오류 코드를 메시지로 바꾸는 함수(없으면 nullptr)
std::wstring FormatReport(const Report& r, std::wstring (*win32Message)(uint32_t));
bool OutcomeLeavesChanges(Outcome o);  // 변경 사항이 시스템에 남는 결과인지(원상복구 안내 필요)

} // namespace macchg
