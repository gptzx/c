#include "engine.h"

namespace macchg {

Engine::Engine(IRegistryBackend& reg, IAdapterControl& dev, ISystem& sys, SessionBackupStore& store)
    : reg_(reg), dev_(dev), sys_(sys), store_(store) {}

Engine::VerifyResult Engine::WaitForMac(const AdapterIdentity& id, const std::optional<Mac>& expected, uint32_t timeoutMs, uint32_t pollMs, uint32_t settleMs) {
    VerifyResult r;
    const uint64_t start = sys_.NowMs();
    std::optional<uint64_t> firstSeenAt;
    for (;;) {
        AdapterState st;
        Status s = dev_.QueryState(id, st);
        if (s.ok() && st.present && st.enabled && st.currentMac.has_value()) {
            r.observed = true;
            r.last = st;
            if (expected.has_value() && *st.currentMac == *expected) { r.match = true; return r; }
            // 기대값이 없거나 불일치: 잠시 안정화 후 그대로 보고(불일치/확인불가)
            uint64_t now = sys_.NowMs();
            if (!firstSeenAt) firstSeenAt = now;
            if (now - *firstSeenAt >= settleMs) return r;
        } else if (s.ok()) {
            r.last = st;
        }
        uint64_t now = sys_.NowMs();
        if (now - start >= timeoutMs) { r.timedOut = !r.observed; return r; }
        sys_.SleepMs(pollMs);
    }
}

Status Engine::ApplyRegistryBackup(const AdapterBackup& b, std::vector<std::wstring>& remaining) {
    remaining.clear();
    Status first = Status::Ok();
    const RegKeyId classKey = NetClassDriverKey(b.identity.driverKeyIndex);
    if (b.networkAddressExisted) {
        RegValue v = b.networkAddress;
        v.name = L"NetworkAddress";
        Status s = reg_.WriteValue(classKey, v);
        if (!s.ok()) { first = s; remaining.push_back(L"NetworkAddress (복원 실패)"); }
    } else {
        bool existed = false;
        Status s = reg_.DeleteValue(classKey, L"NetworkAddress", existed);
        if (!s.ok()) { first = s; remaining.push_back(L"NetworkAddress (삭제 실패)"); }
    }
    if (b.tcpipKeyExisted) {
        const RegKeyId tcpKey = TcpipInterfaceKey(b.identity.interfaceGuid);
        bool exists = false;
        Status s = reg_.KeyExists(tcpKey, exists);
        if (!s.ok()) { if (first.ok()) first = s; remaining.push_back(L"Tcpip\\Parameters\\Interfaces\\{GUID} (접근 실패)"); }
        else if (!exists) { remaining.push_back(L"Tcpip\\Parameters\\Interfaces\\{GUID} (키가 사라짐 — 키는 만들지 않음)"); if (first.ok()) first = Status::Fail(APP_E_RESTORE_MISMATCH, L"TcpipKeyMissing"); }
        else {
            std::vector<std::wstring> mism;
            s = RestoreValueSnapshot(reg_, tcpKey, b.tcpipValues, mism);
            if (!s.ok()) { if (first.ok()) first = s; for (auto& m : mism) remaining.push_back(L"Tcpip 값: " + (m.empty() ? std::wstring(L"(기본값)") : m)); if (mism.empty()) remaining.push_back(L"Tcpip 값 복원 오류"); }
        }
    }
    return first;
}

Report Engine::Change(const ChangeRequest& req, const ProgressFn& progress) {
    Report r;
    const AdapterIdentity& id = req.identity;
    const RegKeyId classKey = NetClassDriverKey(id.driverKeyIndex);
    const RegKeyId tcpKey = TcpipInterfaceKey(id.interfaceGuid);
    if (req.mode == ChangeRequest::Mode::SetMac) r.requestedMac = req.newMac;

    // ---------- 1. 검증 ----------
    Emit(progress, Phase::Validating, L"대상·권한·입력을 검증하는 중…");
    if (req.mode == ChangeRequest::Mode::SetMac) {
        Status v = ValidateUserMac(req.newMac);
        if (!v.ok()) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Validate; r.error = v; return r; }
    }
    {
        Status v = dev_.VerifyIdentity(id);
        if (!v.ok()) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Validate; r.error = v; return r; }
    }
    if (Cancelled(req.cancel)) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Validate; r.error = Status::Fail(APP_E_CANCELLED, L"Validate"); return r; }

    // ---------- 2. 백업 ----------
    Emit(progress, Phase::BackingUp, L"변경 전 상태를 백업하는 중…");
    AdapterBackup undo;
    {
        Status b = CaptureBackup(reg_, dev_, id, req.displayName, undo);
        if (!b.ok()) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Backup; r.error = b; return r; }
    }
    r.previousMac = undo.macAtBackup;
    CleanupPlan plan;
    const bool doCleanup = req.cleanupTcpip && undo.tcpipKeyExisted;
    if (doCleanup) {
        plan = PlanTcpipCleanup(undo.tcpipValues);
        r.enableDhcpPreserved = plan.enableDhcpPresent;
        r.staticSettings = DetectStaticSettings(undo.tcpipValues);
    }
    if (Cancelled(req.cancel)) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Backup; r.error = Status::Fail(APP_E_CANCELLED, L"Backup"); return r; }

    // 백업이 온전히 확보된 지금 시점에 세션 기준(최초 변경 전 상태)을 기록한다. 이미 있으면 덮어쓰지 않는다.
    const bool firstChange = store_.SetOriginalIfAbsent(undo);
    r.baselineRecorded = firstChange;

    // 보상 복구에 필요한 진행 상태
    bool disabledByUs = false;      // 우리가 중지시킴
    bool registryTouched = false;   // NetworkAddress 또는 TCP/IP 값을 변경함
    bool deviceEnabledNow = undo.adapterWasEnabled;

    // 보상 복구: 레지스트리를 undo 로 되돌리고, 원래 활성화였다면 다시 켠다.
    auto rollback = [&](Report& rep) -> bool {
        Emit(progress, Phase::RollingBack, L"직전 상태로 복구하는 중…");
        bool allOk = true;
        if (registryTouched) {
            // 드라이버가 레지스트리를 다시 읽도록, 켜져 있으면 먼저 중지한다(실패해도 레지스트리는 되돌린다).
            if (deviceEnabledNow) {
                bool rb = false;
                Status d = dev_.SetEnabled(id, false, rb);
                if (d.ok()) deviceEnabledNow = false; else { allOk = false; if (rep.rollbackError.ok()) rep.rollbackError = d; }
            }
            std::vector<std::wstring> remaining;
            Status a = ApplyRegistryBackup(undo, remaining);
            if (!a.ok()) { allOk = false; if (rep.rollbackError.ok()) rep.rollbackError = a; for (auto& x : remaining) rep.remainingChanges.push_back(x); }
            else registryTouched = false;
        }
        if (undo.adapterWasEnabled && !deviceEnabledNow) {
            bool rb = false;
            Status e = dev_.SetEnabled(id, true, rb);
            if (e.ok()) { deviceEnabledNow = true; rep.rebootRequired = rep.rebootRequired || rb; }
            else { allOk = false; if (rep.rollbackError.ok()) rep.rollbackError = e; rep.remainingChanges.push_back(L"어댑터가 중지 상태로 남아 있음 (장치 관리자에서 '사용'으로 변경)"); }
        } else if (!undo.adapterWasEnabled && deviceEnabledNow) {
            bool rb = false;
            Status d = dev_.SetEnabled(id, false, rb);
            if (d.ok()) deviceEnabledNow = false; else { allOk = false; if (rep.rollbackError.ok()) rep.rollbackError = d; rep.remainingChanges.push_back(L"원래 비활성이던 어댑터가 활성 상태로 남아 있음"); }
        }
        return allOk;
    };

    auto finish = [&](Report& rep, Outcome o) -> Report {
        rep.outcome = o;
        if (firstChange && IsFullyRolledBack(o)) { store_.ClearOriginal(id.interfaceGuid); rep.baselineCleared = true; rep.baselineRecorded = false; }
        Emit(progress, Phase::Done, L"완료");
        return rep;
    };

    // ---------- 3. 어댑터 중지 ----------
    if (undo.adapterWasEnabled) {
        Emit(progress, Phase::Disabling, L"선택한 어댑터를 중지하는 중… (네트워크가 잠시 끊깁니다)");
        bool rb = false;
        Status d = dev_.SetEnabled(id, false, rb);
        if (!d.ok()) {
            r.failedStep = Step::Disable; r.error = d;
            AdapterState st;
            if (dev_.QueryState(id, st).ok() && st.present && st.enabled) return finish(r, Outcome::NothingChanged);
            deviceEnabledNow = false;
            return finish(r, rollback(r) ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed);
        }
        disabledByUs = true;
        deviceEnabledNow = false;
        r.rebootRequired = r.rebootRequired || rb;
    } else {
        Emit(progress, Phase::Disabling, L"어댑터가 이미 비활성 상태입니다. 그대로 유지합니다.");
    }
    (void)disabledByUs;

    if (Cancelled(req.cancel)) {
        r.failedStep = Step::WriteNetworkAddress; r.error = Status::Fail(APP_E_CANCELLED, L"BeforeWrite");
        return finish(r, rollback(r) ? Outcome::CancelledRolledBack : Outcome::CancelledRollbackFailed);
    }

    // ---------- 4. NetworkAddress 기록/제거 ----------
    Emit(progress, Phase::Applying, req.mode == ChangeRequest::Mode::SetMac ? L"NetworkAddress 값을 기록하는 중…" : L"NetworkAddress 값을 제거하는 중…");
    {
        Status w;
        if (req.mode == ChangeRequest::Mode::SetMac) w = reg_.WriteValue(classKey, MakeSzValue(L"NetworkAddress", FormatPlain(req.newMac)));
        else { bool existed = false; w = reg_.DeleteValue(classKey, L"NetworkAddress", existed); }
        if (!w.ok()) {
            r.failedStep = Step::WriteNetworkAddress; r.error = w;
            registryTouched = true;  // 부분 변경 가능성을 보수적으로 가정
            return finish(r, rollback(r) ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed);
        }
        registryTouched = true;
    }

    // ---------- 5. TCP/IP 값 삭제 및 검증 ----------
    if (doCleanup) {
        Emit(progress, Phase::CleaningTcpip, L"TCP/IP 인터페이스 값을 정리하는 중… (EnableDHCP 제외)");
        std::vector<std::wstring> remaining;
        bool intact = true;
        Status c = ExecuteTcpipCleanup(reg_, tcpKey, plan, remaining, intact);
        for (const RegValue& v : plan.toDelete) r.deletedValues.push_back(v.name);
        r.enableDhcpIntact = intact;
        if (!c.ok()) {
            r.failedStep = Step::CleanupTcpip; r.error = c;
            for (auto& n : remaining) r.remainingChanges.push_back(L"삭제되지 않은 Tcpip 값: " + (n.empty() ? std::wstring(L"(기본값)") : n));
            bool ok = rollback(r);
            if (ok) r.remainingChanges.clear();
            return finish(r, ok ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed);
        }
    } else if (req.cleanupTcpip) {
        Emit(progress, Phase::CleaningTcpip, L"TCP/IP 인터페이스 키가 없어 정리할 값이 없습니다.");
    }

    if (Cancelled(req.cancel)) {
        r.failedStep = Step::Enable; r.error = Status::Fail(APP_E_CANCELLED, L"BeforeEnable");
        return finish(r, rollback(r) ? Outcome::CancelledRolledBack : Outcome::CancelledRollbackFailed);
    }

    // ---------- 6. 어댑터 재활성화 ----------
    if (undo.adapterWasEnabled) {
        Emit(progress, Phase::Enabling, L"어댑터를 다시 활성화하는 중…");
        bool rb = false;
        Status e = dev_.SetEnabled(id, true, rb);
        if (!e.ok()) {
            r.failedStep = Step::Enable; r.error = e;
            // 레지스트리를 되돌리고 다시 켜기를 시도한다.
            bool ok = rollback(r);
            return finish(r, ok ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed);
        }
        deviceEnabledNow = true;
        r.rebootRequired = r.rebootRequired || rb;
    } else {
        // 원래 비활성: 그대로 둔다. 실제 MAC 은 확인할 수 없다.
        AdapterState st;
        if (dev_.QueryState(id, st).ok()) r.rebootRequired = r.rebootRequired || st.rebootRequired;
        return finish(r, Outcome::AppliedAdapterDisabled);
    }

    // ---------- 7. 실제 MAC·연결 상태 확인 ----------
    Emit(progress, Phase::Verifying, L"실제 MAC 과 연결 상태를 확인하는 중…");
    std::optional<Mac> expected;
    if (req.mode == ChangeRequest::Mode::SetMac) expected = req.newMac; else expected = req.expectedMacAfterRemove;
    VerifyResult vr = WaitForMac(id, expected, req.verifyTimeoutMs, req.pollIntervalMs, req.settleMs);
    r.observedMac = vr.last.currentMac;
    r.operUp = vr.last.operUp;
    r.rebootRequired = r.rebootRequired || vr.last.rebootRequired;

    if (vr.match) return finish(r, Outcome::Success);
    if (!vr.observed) {
        r.failedStep = Step::Verify;
        if (!vr.last.present) r.error = Status::Fail(APP_E_ADAPTER_NOT_FOUND, L"Verify");
        else r.error = Status::Fail(APP_E_VERIFY_TIMEOUT, L"Verify");
        return finish(r, Outcome::VerifyTimeout);
    }
    if (!expected.has_value()) return finish(r, Outcome::AppliedUnverified);
    if (r.rebootRequired) return finish(r, Outcome::RebootRequired);

    // 드라이버가 값을 무시했다: 직전 상태로 되돌린다.
    r.failedStep = Step::Verify;
    r.error = Status::Fail(APP_E_DRIVER_IGNORED, L"Verify");
    bool ok = rollback(r);
    return finish(r, ok ? Outcome::DriverIgnoredRolledBack : Outcome::DriverIgnoredRollbackFailed);
}

Report Engine::Restore(const RestoreRequest& req, const ProgressFn& progress) {
    Report r;
    const AdapterIdentity& id = req.identity;
    const AdapterBackup& base = req.baseline;

    Emit(progress, Phase::Validating, L"복구 대상을 검증하는 중…");
    if (base.identity != id) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Validate; r.error = Status::Fail(APP_E_IDENTITY_MISMATCH, L"Restore/BaselineIdentity"); return r; }
    {
        Status v = dev_.VerifyIdentity(id);
        if (!v.ok()) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Validate; r.error = v; return r; }
    }
    if (Cancelled(req.cancel)) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Validate; r.error = Status::Fail(APP_E_CANCELLED, L"Validate"); return r; }

    // 복구 작업 자체의 실패를 되돌리기 위한 직전 상태 백업(세션 기준과는 별도)
    Emit(progress, Phase::BackingUp, L"복구 직전 상태를 백업하는 중…");
    AdapterBackup undo;
    {
        Status b = CaptureBackup(reg_, dev_, id, base.adapterName, undo);
        if (!b.ok()) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Backup; r.error = b; return r; }
    }
    r.previousMac = undo.macAtBackup;
    if (Cancelled(req.cancel)) { r.outcome = Outcome::NothingChanged; r.failedStep = Step::Backup; r.error = Status::Fail(APP_E_CANCELLED, L"Backup"); return r; }

    bool deviceEnabledNow = undo.adapterWasEnabled;
    bool registryTouched = false;

    auto rollback = [&](Report& rep) -> bool {
        Emit(progress, Phase::RollingBack, L"복구 직전 상태로 되돌리는 중…");
        bool allOk = true;
        if (registryTouched) {
            std::vector<std::wstring> remaining;
            Status a = ApplyRegistryBackup(undo, remaining);
            if (!a.ok()) { allOk = false; if (rep.rollbackError.ok()) rep.rollbackError = a; for (auto& x : remaining) rep.remainingChanges.push_back(x); }
        }
        if (undo.adapterWasEnabled && !deviceEnabledNow) {
            bool rb = false;
            Status e = dev_.SetEnabled(id, true, rb);
            if (e.ok()) { deviceEnabledNow = true; rep.rebootRequired = rep.rebootRequired || rb; }
            else { allOk = false; if (rep.rollbackError.ok()) rep.rollbackError = e; rep.remainingChanges.push_back(L"어댑터가 중지 상태로 남아 있음 (장치 관리자에서 '사용'으로 변경)"); }
        }
        return allOk;
    };

    // 1) 켜져 있으면 중지
    if (undo.adapterWasEnabled) {
        Emit(progress, Phase::Disabling, L"어댑터를 중지하는 중… (네트워크가 잠시 끊깁니다)");
        bool rb = false;
        Status d = dev_.SetEnabled(id, false, rb);
        if (!d.ok()) {
            r.failedStep = Step::Disable; r.error = d;
            AdapterState st;
            if (dev_.QueryState(id, st).ok() && st.present && st.enabled) { r.outcome = Outcome::NothingChanged; return r; }
            deviceEnabledNow = false;
            r.outcome = rollback(r) ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed;
            return r;
        }
        deviceEnabledNow = false;
        r.rebootRequired = r.rebootRequired || rb;
    }

    if (Cancelled(req.cancel)) {
        r.failedStep = Step::RestoreRegistry; r.error = Status::Fail(APP_E_CANCELLED, L"BeforeRestore");
        r.outcome = rollback(r) ? Outcome::CancelledRolledBack : Outcome::CancelledRollbackFailed;
        return r;
    }

    // 2) 레지스트리 복원 (NetworkAddress + TCP/IP 값)
    Emit(progress, Phase::Applying, L"NetworkAddress 와 TCP/IP 값을 복원하는 중…");
    registryTouched = true;
    {
        std::vector<std::wstring> remaining;
        Status a = ApplyRegistryBackup(base, remaining);
        if (!a.ok()) {
            r.failedStep = Step::RestoreRegistry; r.error = a;
            for (auto& x : remaining) r.remainingChanges.push_back(x);
            bool ok = rollback(r);
            if (ok) r.remainingChanges.clear();
            r.outcome = ok ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed;
            return r;
        }
    }

    // 3) 원래 활성화였다면 다시 켬. 원래 비활성이면 비활성 유지.
    if (base.adapterWasEnabled) {
        Emit(progress, Phase::Enabling, L"어댑터를 다시 활성화하는 중…");
        bool rb = false;
        Status e = dev_.SetEnabled(id, true, rb);
        if (!e.ok()) {
            r.failedStep = Step::Enable; r.error = e;
            bool ok = rollback(r);
            r.outcome = ok ? Outcome::FailedRolledBack : Outcome::FailedRollbackFailed;
            return r;
        }
        deviceEnabledNow = true;
        r.rebootRequired = r.rebootRequired || rb;
    } else {
        if (req.clearSessionBaselineOnSuccess) { store_.ClearOriginal(id.interfaceGuid); r.baselineCleared = true; }
        r.outcome = Outcome::RestoredUnverified;
        Emit(progress, Phase::Done, L"완료");
        return r;
    }

    // 4) 검증: 기준 MAC 을 알면 비교
    Emit(progress, Phase::Verifying, L"실제 MAC 과 연결 상태를 확인하는 중…");
    VerifyResult vr = WaitForMac(id, base.macAtBackup, req.verifyTimeoutMs, req.pollIntervalMs, req.settleMs);
    r.observedMac = vr.last.currentMac;
    r.operUp = vr.last.operUp;
    r.rebootRequired = r.rebootRequired || vr.last.rebootRequired;
    if (req.clearSessionBaselineOnSuccess) { store_.ClearOriginal(id.interfaceGuid); r.baselineCleared = true; }
    if (vr.match) r.outcome = Outcome::Restored;
    else if (!vr.observed || !base.macAtBackup.has_value()) r.outcome = Outcome::RestoredUnverified;
    else r.outcome = Outcome::RestoredMacMismatch;
    Emit(progress, Phase::Done, L"완료");
    return r;
}

} // namespace macchg
