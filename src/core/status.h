// status.h - 공통 상태/오류 코드 (플랫폼 독립)
#pragma once
#include <cstdint>
#include <string>
#include <utility>

namespace macchg {

// Win32 오류 코드(0 = 성공)와 앱 정의 코드를 함께 담는다.
struct Status {
    uint32_t code = 0;     // 0 == 성공. 그 외에는 Win32 오류 코드 또는 AppError.
    std::wstring where;    // 실패 위치(API 이름/단계). 성공 시 비어 있음.

    bool ok() const { return code == 0; }
    static Status Ok() { return Status{}; }
    static Status Fail(uint32_t c, std::wstring w) { return Status{c, std::move(w)}; }
};

// 앱 정의 오류 코드. Win32 시스템 오류 코드 범위(0..15999)와 겹치지 않도록 상위 비트를 사용한다.
enum AppError : uint32_t {
    APP_E_BASE = 0x20000000,
    APP_E_INVALID_MAC,            // MAC 형식 오류
    APP_E_RNG_FAILED,             // 암호학적 난수 생성 실패 (대체 생성 없음)
    APP_E_MAC_COLLISION,          // 중복되지 않는 MAC 생성 실패
    APP_E_IDENTITY_MISMATCH,      // GUID ↔ 장치 인스턴스 ↔ 드라이버 키 대응 확정 실패
    APP_E_ADAPTER_NOT_FOUND,      // 어댑터가 더 이상 존재하지 않음
    APP_E_AMBIGUOUS_DEVICE,       // 같은 GUID를 가진 장치가 둘 이상
    APP_E_CLEANUP_INCOMPLETE,     // TCP/IP 값 삭제 검증 실패(남은 값 존재)
    APP_E_VERIFY_TIMEOUT,         // 재활성화 후 실제 MAC 확인 시간 초과
    APP_E_DRIVER_IGNORED,         // 드라이버가 NetworkAddress 값을 무시함
    APP_E_CANCELLED,              // 사용자 취소
    APP_E_BACKUP_INCOMPLETE,      // 백업 스냅샷 불일치
    APP_E_NO_ORIGINAL_BACKUP,     // 복구 기준(최초 변경 전 상태)이 없음
    APP_E_FILE_FORMAT,            // 복구 파일 형식/스키마 오류
    APP_E_FILE_MACHINE_MISMATCH,  // 복구 파일의 PC 식별 정보 불일치
    APP_E_FILE_ADAPTER_MISMATCH,  // 복구 파일의 어댑터 식별 정보 불일치
    APP_E_BUSY,                   // 다른 작업 진행 중
    APP_E_RESTORE_MISMATCH,       // 복원 후 검증 불일치
    APP_E_UNSUPPORTED_WOW64,      // 32비트 빌드가 64비트 Windows에서 실행됨
    APP_E_DEVICE_DISABLED_LEFT,   // 어댑터가 중지 상태로 남음(복구 실패)
    APP_E_NOT_ELEVATED,           // 관리자 권한 없음
    APP_E_UNEXPECTED_STATE,       // 예상치 못한 상태(검증 로직)
};

} // namespace macchg
