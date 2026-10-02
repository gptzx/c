// restore_file.h - 복구 파일(사용자가 명시적으로 내보내기/불러오기) 직렬화
//
// 형식: UTF-8 텍스트, 줄 단위 key=value. 버전 있음. 값 이름은 UTF-16LE 16진수로, 데이터는 16진수로
// 인코딩하여 어떤 문자/바이트도 손실 없이 보존한다. 파일에는 레지스트리 "경로"가 들어가지 않는다.
// 불러온 백업은 코어의 형식화된 키(드라이버 키 인덱스, 인터페이스 GUID)로만 적용되며,
// 그 식별자도 현재 PC 에서 실제로 열거된 어댑터와 일치할 때만 사용한다.
#pragma once
#include <string>
#include <vector>
#include "backup.h"

namespace macchg {

struct MachineIdentity {
    std::wstring machineGuid;   // HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (읽기 전용)
    std::wstring computerName;
};

struct RestoreFile {
    static constexpr uint32_t kFormatVersion = 1;
    uint32_t formatVersion = kFormatVersion;
    MachineIdentity machine;
    AdapterBackup backup;
    std::wstring createdAt;     // 표시용 문자열(검증에 사용하지 않음)
    std::wstring appVersion;    // 표시용
};

std::string SerializeRestoreFile(const RestoreFile& f);
// 형식/스키마 오류 → APP_E_FILE_FORMAT. error 에 사람이 읽을 설명.
Status ParseRestoreFile(const std::string& bytes, RestoreFile& out, std::wstring& error);

// 현재 PC·어댑터와 대조한다. live 는 현재 열거로 얻은 식별 정보.
Status ValidateRestoreFile(const RestoreFile& f, const MachineIdentity& currentMachine, const std::vector<AdapterIdentity>& liveAdapters, std::wstring& error);

} // namespace macchg
