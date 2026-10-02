// main.cpp - MacChanger 한국어 Win32 GUI
//
// 원칙:
//  - 설정 파일/레지스트리/AppData/로그 파일을 만들지 않는다. 복구 데이터는 메모리에만 둔다.
//  - 작업은 워커 스레드에서 실행하고, 진행 중에는 버튼을 잠가 중복 클릭·동시 변경을 막는다.
//  - 취소 요청은 엔진의 단계 경계에서만 반영되며 어댑터가 중지된 채 남지 않도록 보상 복구가 수행된다.
#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <shellapi.h>
#include <process.h>
#include <atomic>
#include <cstdio>
#include <cwchar>
#include <functional>
#include <memory>
#include <string>
#include <vector>

#include "core/describe_ko.h"
#include "core/engine.h"
#include "core/restore_file.h"
#include "core/utf.h"
#include "win/win_adapters.h"
#include "win/win_device.h"
#include "win/win_random.h"
#include "win/win_registry.h"
#include "win/win_system.h"
#include "resource.h"

using namespace macchg;

namespace {

const wchar_t* const kAppTitle = L"MacChanger - 포터블 MAC 주소 변경";
const wchar_t* const kAppVersion = L"1.0.0";

enum : int {
    IDC_LIST = 1001, IDC_REFRESH, IDC_SHOWALL, IDC_DETAILS,
    IDC_CURMAC, IDC_NEWMAC, IDC_RANDOM, IDC_FIX02, IDC_CHANGE, IDC_RANDOM_CHANGE, IDC_RESTORE, IDC_CANCEL,
    IDC_STATUS, IDC_LOG, IDC_NOTE, IDC_NEWMAC_COLON,
};
enum : int { IDM_EXIT = 2001, IDM_EXPORT, IDM_IMPORT, IDM_FACTORY, IDM_ABOUT, IDM_CLEARLOG };
enum : UINT { WM_APP_PROGRESS = WM_APP + 1, WM_APP_DONE, WM_APP_ADAPTERS, WM_APP_LOG };
enum class Job { None, Refresh, Change, Restore, RestoreAllThenClose, Factory };

struct DoneMsg {
    Job job = Job::None;
    Report report;
    std::wstring adapterGuid;
    bool restoreAllFailed = false;
};

struct App {
    HWND hwnd = nullptr, list = nullptr, curMac = nullptr, newMac = nullptr, newMacColon = nullptr, status = nullptr, log = nullptr, note = nullptr;
    HWND btnRefresh = nullptr, chkShowAll = nullptr, btnDetails = nullptr, btnRandom = nullptr, chkFix02 = nullptr, btnChange = nullptr, btnRandomChange = nullptr, btnRestore = nullptr, btnCancel = nullptr;
    HWND lblCur = nullptr, lblNew = nullptr, lblList = nullptr;
    HFONT font = nullptr;

    std::vector<AdapterInfo> adapters;
    std::vector<int> visible;      // 목록에 표시된 adapters 인덱스
    std::wstring selectedGuid;     // 새로 고침 후 선택 유지용

    SessionBackupStore store;
    WinRegistryBackend reg = WinRegistryBackend::ForSystem();
    WinAdapterControl dev;
    WinRandomSource rng;
    WinSystem sys;

    std::atomic<bool> busy{false};
    std::atomic<bool> cancel{false};
    Job currentJob = Job::None;
    HANDLE worker = nullptr;
    bool pendingClose = false;
    bool canControl = true;
    std::wstring controlBlockReason;
    bool pendingRandomChange = false;   // 랜덤 생성 후 즉시 변경 흐름
};

App g;

// ------------------------------------------------------------------ 유틸
std::wstring GetText(HWND h) {
    int n = GetWindowTextLengthW(h);
    std::wstring s(static_cast<size_t>(n) + 1, L'\0');
    GetWindowTextW(h, s.data(), n + 1);
    s.resize(static_cast<size_t>(n));
    return s;
}

void AppendLog(const std::wstring& text) {
    std::wstring line = L"[" + LocalTimestampString().substr(11) + L"] " + text;
    // CRLF 정규화
    std::wstring norm;
    for (size_t i = 0; i < line.size(); ++i) {
        if (line[i] == L'\n' && (i == 0 || line[i - 1] != L'\r')) norm += L"\r\n";
        else norm.push_back(line[i]);
    }
    if (norm.size() < 2 || norm.substr(norm.size() - 2) != L"\r\n") norm += L"\r\n";
    int len = GetWindowTextLengthW(g.log);
    SendMessageW(g.log, EM_SETSEL, len, len);
    SendMessageW(g.log, EM_REPLACESEL, FALSE, reinterpret_cast<LPARAM>(norm.c_str()));
    SendMessageW(g.log, EM_SCROLLCARET, 0, 0);
}

void SetStatus(const std::wstring& s) { SetWindowTextW(g.status, (L"상태: " + s).c_str()); }

int SelectedIndex() {
    int item = ListView_GetNextItem(g.list, -1, LVNI_SELECTED);
    if (item < 0 || item >= static_cast<int>(g.visible.size())) return -1;
    return g.visible[static_cast<size_t>(item)];
}

const AdapterInfo* SelectedAdapter() {
    int i = SelectedIndex();
    return i < 0 ? nullptr : &g.adapters[static_cast<size_t>(i)];
}

HFONT CreateUiFont() {
    NONCLIENTMETRICSW ncm{};
    ncm.cbSize = sizeof(ncm);
    if (SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0)) return CreateFontIndirectW(&ncm.lfMessageFont);
    return static_cast<HFONT>(GetStockObject(DEFAULT_GUI_FONT));
}

void ApplyFont(HWND h) { SendMessageW(h, WM_SETFONT, reinterpret_cast<WPARAM>(g.font), TRUE); }

int MsgBox(const std::wstring& text, UINT flags) { return MessageBoxW(g.hwnd, text.c_str(), kAppTitle, flags); }

// TaskDialogIndirect 를 동적으로 호출(없으면 MessageBox 폴백). 반환: 1=복구 후 종료, 2=유지 후 종료, 0=취소
int AskExitWithChanges(const std::wstring& content) {
    typedef HRESULT(WINAPI * TDI)(const TASKDIALOGCONFIG*, int*, int*, BOOL*);
    HMODULE cc = GetModuleHandleW(L"comctl32.dll");
    TDI tdi = cc ? reinterpret_cast<TDI>(reinterpret_cast<void*>(GetProcAddress(cc, "TaskDialogIndirect"))) : nullptr;
    if (tdi) {
        TASKDIALOG_BUTTON btns[] = { {101, L"복구하고 종료\n변경한 어댑터를 모두 변경 전 상태로 되돌린 뒤 종료합니다."}, {102, L"변경 유지 후 종료\n변경을 그대로 두고 종료합니다. 메모리 백업은 사라집니다."} };
        TASKDIALOGCONFIG cfg{};
        cfg.cbSize = sizeof(cfg);
        cfg.hwndParent = g.hwnd;
        cfg.dwFlags = TDF_USE_COMMAND_LINKS | TDF_ALLOW_DIALOG_CANCELLATION;
        cfg.dwCommonButtons = TDCBF_CANCEL_BUTTON;
        cfg.pszWindowTitle = kAppTitle;
        cfg.pszMainIcon = TD_WARNING_ICON;
        cfg.pszMainInstruction = L"원상복구하지 않은 변경이 있습니다";
        cfg.pszContent = content.c_str();
        cfg.cButtons = 2;
        cfg.pButtons = btns;
        cfg.nDefaultButton = 101;
        int pressed = 0;
        if (SUCCEEDED(tdi(&cfg, &pressed, nullptr, nullptr))) {
            if (pressed == 101) return 1;
            if (pressed == 102) return 2;
            return 0;
        }
    }
    int r = MsgBox(content + L"\n\n[예] 복구하고 종료   [아니요] 변경 유지 후 종료   [취소] 돌아가기", MB_YESNOCANCEL | MB_ICONWARNING | MB_DEFBUTTON1);
    if (r == IDYES) return 1;
    if (r == IDNO) return 2;
    return 0;
}

// ------------------------------------------------------------------ 워커
struct WorkerCtx { std::function<void()> fn; };

unsigned __stdcall WorkerThunk(void* p) {
    std::unique_ptr<WorkerCtx> ctx(static_cast<WorkerCtx*>(p));
    ctx->fn();
    return 0;
}

void PostProgress(Phase ph, const std::wstring& msg) {
    PostMessageW(g.hwnd, WM_APP_PROGRESS, static_cast<WPARAM>(ph), reinterpret_cast<LPARAM>(new std::wstring(msg)));
}

void PostLogLine(const std::wstring& msg) { PostMessageW(g.hwnd, WM_APP_LOG, 0, reinterpret_cast<LPARAM>(new std::wstring(msg))); }

bool StartWorker(Job job, std::function<void()> fn) {
    if (g.busy.exchange(true)) return false;
    g.cancel.store(false);
    g.currentJob = job;
    if (g.worker) { CloseHandle(g.worker); g.worker = nullptr; }
    auto* ctx = new WorkerCtx{std::move(fn)};
    uintptr_t h = _beginthreadex(nullptr, 0, WorkerThunk, ctx, 0, nullptr);
    if (!h) { delete ctx; g.busy.store(false); g.currentJob = Job::None; return false; }
    g.worker = reinterpret_cast<HANDLE>(h);
    return true;
}

// ------------------------------------------------------------------ 목록
void UpdateButtons() {
    const AdapterInfo* a = SelectedAdapter();
    bool busy = g.busy.load();
    bool canChange = !busy && a && a->deviceResolved && !a->ambiguous && g.canControl;
    EnableWindow(g.btnChange, canChange);
    EnableWindow(g.btnRandomChange, canChange);
    EnableWindow(g.btnRandom, !busy && a != nullptr);
    EnableWindow(g.btnRestore, !busy && a && g.canControl && g.store.HasOriginal(a->id.interfaceGuid));
    EnableWindow(g.btnRefresh, !busy);
    EnableWindow(g.btnDetails, !busy && a != nullptr);
    EnableWindow(g.chkShowAll, !busy);
    EnableWindow(g.newMac, !busy);
    EnableWindow(g.chkFix02, !busy);
    EnableWindow(g.list, !busy);
    EnableWindow(g.btnCancel, busy && (g.currentJob == Job::Change || g.currentJob == Job::Restore || g.currentJob == Job::Factory));
    HMENU menu = GetMenu(g.hwnd);
    if (menu) {
        EnableMenuItem(menu, IDM_EXPORT, (!busy && a && g.store.HasOriginal(a->id.interfaceGuid)) ? MF_ENABLED : MF_GRAYED);
        EnableMenuItem(menu, IDM_IMPORT, (!busy && g.canControl) ? MF_ENABLED : MF_GRAYED);
        EnableMenuItem(menu, IDM_FACTORY, canChange ? MF_ENABLED : MF_GRAYED);
    }
}

void UpdateSelectionInfo() {
    const AdapterInfo* a = SelectedAdapter();
    if (!a) { SetWindowTextW(g.curMac, L""); SetWindowTextW(g.note, L"어댑터를 선택하세요."); UpdateButtons(); return; }
    g.selectedGuid = a->id.interfaceGuid;
    SetWindowTextW(g.curMac, a->currentMac ? (FormatPlain(*a->currentMac) + L"   (" + FormatColon(*a->currentMac) + L")").c_str() : (a->enabled ? L"확인 불가" : L"확인 불가 (장치 비활성)"));
    std::wstring note;
    if (!a->deviceResolved) note = a->ambiguous ? L"같은 GUID 를 가진 장치가 둘 이상이어서 대상을 확정할 수 없습니다. 변경할 수 없습니다." : L"이 인터페이스는 PnP 장치/드라이버 키와 1:1 로 대응되지 않아 변경할 수 없습니다.";
    else if (!a->isPhysical) note = L"가상/미확인 어댑터입니다. 변경은 가능하지만 결과는 드라이버에 따라 다릅니다.";
    else if (a->kind == AdapterInfo::Kind::Wireless) note = L"무선랜: 드라이버가 특정 값만 받아들일 수 있습니다. 실패하면 '첫 바이트 02 고정' 옵션으로 다시 시도하세요. 지원 여부는 미확인입니다.";
    else note = L"변경 중 네트워크가 잠시 끊깁니다. 변경 시 이 어댑터의 TCP/IP 인터페이스 값(EnableDHCP 제외)이 함께 삭제됩니다.";
    if (!a->enabled) note += L" 현재 장치가 비활성 상태이므로 변경 후 실제 MAC 을 확인할 수 없습니다.";
    if (g.store.HasOriginal(a->id.interfaceGuid)) note += L" [이 세션에서 변경됨 — 원상복구 가능]";
    SetWindowTextW(g.note, note.c_str());
    UpdateButtons();
}

void FillList() {
    bool showAll = SendMessageW(g.chkShowAll, BM_GETCHECK, 0, 0) == BST_CHECKED;
    ListView_DeleteAllItems(g.list);
    g.visible.clear();
    int selectItem = -1;
    for (size_t i = 0; i < g.adapters.size(); ++i) {
        const AdapterInfo& a = g.adapters[i];
        bool primary = a.deviceResolved && a.isPhysical && (a.kind == AdapterInfo::Kind::Ethernet || a.kind == AdapterInfo::Kind::Wireless);
        if (!showAll && !primary) continue;
        LVITEMW it{};
        it.mask = LVIF_TEXT | LVIF_PARAM;
        it.iItem = static_cast<int>(g.visible.size());
        it.lParam = static_cast<LPARAM>(i);
        std::wstring kind = KindLabel(a.kind);
        if (!a.isPhysical) kind += a.deviceResolved ? L" (가상)" : L" (식별 불가)";
        it.pszText = const_cast<LPWSTR>(kind.c_str());
        int row = ListView_InsertItem(g.list, &it);
        std::wstring name = a.friendlyName.empty() ? a.description : a.friendlyName;
        ListView_SetItemText(g.list, row, 1, const_cast<LPWSTR>(name.c_str()));
        ListView_SetItemText(g.list, row, 2, const_cast<LPWSTR>(a.description.c_str()));
        std::wstring st = OperStatusLabel(a.operStatus, a.enabled, a.interfaceVisible);
        ListView_SetItemText(g.list, row, 3, const_cast<LPWSTR>(st.c_str()));
        std::wstring mac = a.currentMac ? FormatColon(*a.currentMac) : L"확인 불가";
        ListView_SetItemText(g.list, row, 4, const_cast<LPWSTR>(mac.c_str()));
        std::wstring extra;
        if (g.store.HasOriginal(a.id.interfaceGuid)) extra = L"변경됨(복구 가능)";
        else if (a.networkAddressValue) extra = L"NetworkAddress 값 있음";
        if (a.device.rebootRequired) extra += extra.empty() ? L"재부팅 필요" : L", 재부팅 필요";
        ListView_SetItemText(g.list, row, 5, const_cast<LPWSTR>(extra.c_str()));
        if (!g.selectedGuid.empty() && NameEqualsNoCase(a.id.interfaceGuid, g.selectedGuid)) selectItem = row;
        g.visible.push_back(static_cast<int>(i));
    }
    if (selectItem < 0 && !g.visible.empty()) selectItem = 0;
    if (selectItem >= 0) { ListView_SetItemState(g.list, selectItem, LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED); ListView_EnsureVisible(g.list, selectItem, FALSE); }
    UpdateSelectionInfo();
}

void RefreshAdapters() {
    if (!StartWorker(Job::Refresh, [] {
            auto* list = new std::vector<AdapterInfo>();
            Status s = EnumerateAdapters(*list);
            if (!s.ok()) PostLogLine(L"어댑터 열거 실패: " + DescribeError(s) + L" — " + Win32ErrorMessage(s.code));
            PostMessageW(g.hwnd, WM_APP_ADAPTERS, 0, reinterpret_cast<LPARAM>(list));
        })) return;
    SetStatus(L"어댑터 조회 중…");
    UpdateButtons();
}

// ------------------------------------------------------------------ 동작
std::vector<Mac> CollectAvoidMacs() {
    std::vector<Mac> v;
    for (const auto& a : g.adapters) { if (a.currentMac) v.push_back(*a.currentMac); if (a.permanentMac) v.push_back(*a.permanentMac); if (a.ndisCurrentMac) v.push_back(*a.ndisCurrentMac); }
    return v;
}

bool DoRandom() {
    RandomMacOptions opt;
    opt.fixFirstByte02 = SendMessageW(g.chkFix02, BM_GETCHECK, 0, 0) == BST_CHECKED;
    Mac m;
    Status s = GenerateRandomMac(g.rng, opt, CollectAvoidMacs(), m);
    if (!s.ok()) { MsgBox(L"랜덤 MAC 생성 실패: " + DescribeError(s), MB_ICONERROR); return false; }
    SetWindowTextW(g.newMac, FormatPlain(m).c_str());
    SetWindowTextW(g.newMacColon, (L"(" + FormatColon(m) + L")").c_str());
    return true;
}

void StartChangeJob(const AdapterInfo& a, const ChangeRequest& reqIn, Job job) {
    ChangeRequest req = reqIn;
    req.cancel = &g.cancel;
    AdapterIdentity id = a.id;
    std::wstring guid = a.id.interfaceGuid;
    if (!StartWorker(job, [req, guid, job] {
            Engine e(g.reg, g.dev, g.sys, g.store);
            Report r = e.Change(req, [](Phase ph, const std::wstring& m) { PostProgress(ph, m); });
            auto* d = new DoneMsg{};
            d->job = job; d->report = r; d->adapterGuid = guid;
            PostMessageW(g.hwnd, WM_APP_DONE, 0, reinterpret_cast<LPARAM>(d));
        })) { MsgBox(L"다른 작업이 진행 중입니다.", MB_ICONINFORMATION); return; }
    SetStatus(L"시작…");
    UpdateButtons();
}

void DoChange() {
    const AdapterInfo* a = SelectedAdapter();
    if (!a || !a->deviceResolved || a->ambiguous) { MsgBox(L"변경할 수 있는 어댑터를 선택하세요.", MB_ICONINFORMATION); return; }
    Mac m;
    std::wstring text = GetText(g.newMac);
    if (!ParseMac(text, m)) { MsgBox(L"새 MAC 형식이 잘못되었습니다. 16진수 12자리(구분자 없음 또는 : - 구분)로 입력하거나 '랜덤 생성'을 누르세요.", MB_ICONWARNING); return; }
    Status v = ValidateUserMac(m);
    if (!v.ok()) { MsgBox(DescribeError(v), MB_ICONWARNING); return; }
    if (a->currentMac && *a->currentMac == m) { MsgBox(L"입력한 MAC 이 현재 MAC 과 같습니다.", MB_ICONINFORMATION); return; }
    if (!IsLocallyAdministered(m)) {
        if (MsgBox(L"입력한 MAC 은 로컬 관리 비트가 꺼진(제조사 OUI 형태) 주소입니다. 일부 드라이버는 거부할 수 있습니다. 계속할까요?", MB_YESNO | MB_ICONQUESTION) != IDYES) return;
    }
    // 고정 IP/DNS 등 삭제 대상 확인 (해당할 때만 확인창)
    std::vector<RegValue> snap;
    bool keyExists = false;
    if (g.reg.KeyExists(TcpipInterfaceKey(a->id.interfaceGuid), keyExists).ok() && keyExists && g.reg.EnumerateValues(TcpipInterfaceKey(a->id.interfaceGuid), snap).ok()) {
        StaticSettingsSummary ss = DetectStaticSettings(snap);
        if (ss.HasAny()) {
            std::wstring msg = L"이 어댑터의 TCP/IP 인터페이스 키에 고정 설정이 있습니다. 변경을 진행하면 EnableDHCP 를 제외한 모든 값이 삭제됩니다(EnableDHCP 값 자체는 바꾸지 않습니다).\n\n";
            if (ss.dhcpDisabled) msg += L"  - EnableDHCP = 0 (고정 IP 사용 중)\n";
            for (const auto& it : ss.items) msg += L"  - " + it + L"\n";
            msg += L"\n삭제된 설정은 '원상복구'로 되돌릴 수 있습니다(이 세션 동안, 또는 복구 파일을 내보낸 경우). 계속할까요?";
            if (MsgBox(msg, MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) return;
        }
        CleanupPlan plan = PlanTcpipCleanup(snap);
        AppendLog(L"삭제 예정 TCP/IP 값 " + std::to_wstring(plan.toDelete.size()) + L"개" + (plan.enableDhcpPresent ? L" (EnableDHCP 보존)" : L" (EnableDHCP 없음 — 만들지 않음)"));
    }
    ChangeRequest req;
    req.identity = a->id;
    req.displayName = a->friendlyName.empty() ? a->description : a->friendlyName;
    req.mode = ChangeRequest::Mode::SetMac;
    req.newMac = m;
    req.cleanupTcpip = true;
    AppendLog(L"변경 시작: " + req.displayName + L" → " + FormatPlain(m) + L" (" + FormatColon(m) + L")");
    StartChangeJob(*a, req, Job::Change);
}

void DoRestore() {
    const AdapterInfo* a = SelectedAdapter();
    if (!a) return;
    const AdapterBackup* b = g.store.Original(a->id.interfaceGuid);
    if (!b) { MsgBox(L"이 어댑터의 변경 전 상태(세션 기준)가 없습니다. 복구 파일이 있으면 고급 > 복구 파일 불러오기를 사용하세요.", MB_ICONINFORMATION); return; }
    RestoreRequest rr;
    rr.identity = a->id;
    rr.baseline = *b;
    rr.cancel = &g.cancel;
    std::wstring guid = a->id.interfaceGuid;
    AppendLog(L"원상복구 시작: " + b->adapterName + L" (기준 MAC " + (b->macAtBackup ? FormatPlain(*b->macAtBackup) : L"미상") + L", TCP/IP 값 " + std::to_wstring(b->tcpipValues.size()) + L"개 복원)");
    if (!StartWorker(Job::Restore, [rr, guid] {
            Engine e(g.reg, g.dev, g.sys, g.store);
            Report r = e.Restore(rr, [](Phase ph, const std::wstring& m) { PostProgress(ph, m); });
            auto* d = new DoneMsg{};
            d->job = Job::Restore; d->report = r; d->adapterGuid = guid;
            PostMessageW(g.hwnd, WM_APP_DONE, 0, reinterpret_cast<LPARAM>(d));
        })) { MsgBox(L"다른 작업이 진행 중입니다.", MB_ICONINFORMATION); return; }
    SetStatus(L"복구 중…");
    UpdateButtons();
}

void DoFactoryMac() {
    const AdapterInfo* a = SelectedAdapter();
    if (!a || !a->deviceResolved) return;
    std::wstring msg = L"[고급] 제조사 기본 MAC 사용\n\n이 기능은 드라이버 키의 NetworkAddress 값만 제거하고 어댑터를 다시 시작합니다. TCP/IP 설정은 건드리지 않으며, '변경 전 상태 복원'(원상복구)과는 다른 기능입니다. 삭제했던 TCP/IP 설정을 되살리지 않습니다.\n\n";
    msg += a->permanentMac ? L"영구 MAC: " + FormatColon(*a->permanentMac) + L" (적용 후 이 값과 비교하여 검증합니다)\n" : L"영구 MAC 을 확인할 수 없어 적용 결과를 검증할 수 없습니다(검증 불가로 보고됨).\n";
    if (!a->networkAddressValue) msg += L"\n현재 NetworkAddress 값이 없습니다. 실행해도 바뀌는 것이 없을 수 있습니다.\n";
    msg += L"\n계속할까요?";
    if (MsgBox(msg, MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
    ChangeRequest req;
    req.identity = a->id;
    req.displayName = a->friendlyName.empty() ? a->description : a->friendlyName;
    req.mode = ChangeRequest::Mode::RemoveNetworkAddress;
    req.cleanupTcpip = false;
    req.expectedMacAfterRemove = a->permanentMac;
    AppendLog(L"[고급] 제조사 기본 MAC 사용 시작: " + req.displayName);
    StartChangeJob(*a, req, Job::Factory);
}

bool WriteWholeFile(const std::wstring& path, const std::string& bytes, DWORD& err) {
    HANDLE h = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) { err = GetLastError(); return false; }
    DWORD written = 0;
    BOOL ok = WriteFile(h, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr);
    err = ok ? 0 : GetLastError();
    CloseHandle(h);
    return ok && written == bytes.size();
}

bool ReadWholeFile(const std::wstring& path, std::string& bytes, DWORD& err) {
    HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) { err = GetLastError(); return false; }
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(h, &size) || size.QuadPart > 64 * 1024 * 1024) { err = ERROR_FILE_TOO_LARGE; CloseHandle(h); return false; }
    bytes.resize(static_cast<size_t>(size.QuadPart));
    DWORD rd = 0;
    BOOL ok = bytes.empty() ? TRUE : ReadFile(h, bytes.data(), static_cast<DWORD>(bytes.size()), &rd, nullptr);
    err = ok ? 0 : GetLastError();
    CloseHandle(h);
    return ok != FALSE;
}

void DoExport() {
    const AdapterInfo* a = SelectedAdapter();
    if (!a) return;
    const AdapterBackup* b = g.store.Original(a->id.interfaceGuid);
    if (!b) { MsgBox(L"내보낼 변경 전 상태가 없습니다(이 세션에서 변경한 어댑터만 내보낼 수 있습니다).", MB_ICONINFORMATION); return; }
    wchar_t path[MAX_PATH] = L"MacChanger-복구.macchg-restore";
    OPENFILENAMEW ofn{};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = g.hwnd;
    ofn.lpstrFilter = L"MacChanger 복구 파일 (*.macchg-restore)\0*.macchg-restore\0모든 파일\0*.*\0";
    ofn.lpstrFile = path;
    ofn.nMaxFile = MAX_PATH;
    ofn.lpstrDefExt = L"macchg-restore";
    ofn.Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetSaveFileNameW(&ofn)) return;
    RestoreFile f;
    f.machine = ReadMachineIdentity();
    f.backup = *b;
    f.createdAt = LocalTimestampString();
    f.appVersion = kAppVersion;
    if (f.machine.machineGuid.empty()) { MsgBox(L"PC 식별 정보(MachineGuid)를 읽을 수 없어 복구 파일을 만들 수 없습니다.", MB_ICONERROR); return; }
    DWORD err = 0;
    if (!WriteWholeFile(path, SerializeRestoreFile(f), err)) { MsgBox(L"파일 쓰기 실패: " + Win32ErrorMessage(err), MB_ICONERROR); return; }
    AppendLog(L"복구 파일 내보내기 완료: " + std::wstring(path));
    MsgBox(L"복구 파일을 저장했습니다. 이 파일에는 변경 전 NetworkAddress 와 TCP/IP 인터페이스 값(IP/DNS 설정 포함 가능)이 들어 있으니 안전하게 보관하세요.", MB_ICONINFORMATION);
}

void DoImport() {
    wchar_t path[MAX_PATH] = L"";
    OPENFILENAMEW ofn{};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = g.hwnd;
    ofn.lpstrFilter = L"MacChanger 복구 파일 (*.macchg-restore)\0*.macchg-restore\0모든 파일\0*.*\0";
    ofn.lpstrFile = path;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetOpenFileNameW(&ofn)) return;
    std::string bytes; DWORD err = 0;
    if (!ReadWholeFile(path, bytes, err)) { MsgBox(L"파일 읽기 실패: " + Win32ErrorMessage(err), MB_ICONERROR); return; }
    RestoreFile f; std::wstring perr;
    Status s = ParseRestoreFile(bytes, f, perr);
    if (!s.ok()) { MsgBox(L"복구 파일을 읽을 수 없습니다: " + perr, MB_ICONERROR); return; }
    std::vector<AdapterIdentity> live;
    for (const auto& a : g.adapters) if (a.deviceResolved) live.push_back(a.id);
    s = ValidateRestoreFile(f, ReadMachineIdentity(), live, perr);
    if (!s.ok()) { MsgBox(L"복구 파일을 적용할 수 없습니다: " + perr, MB_ICONERROR); return; }
    std::wstring guid = f.backup.identity.interfaceGuid;
    if (g.store.HasOriginal(guid)) {
        if (MsgBox(L"이 어댑터에는 이미 이 세션의 변경 전 상태가 있습니다. 파일의 내용으로 바꿀까요? (세션 기준이 파일 내용으로 대체됩니다)", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
    }
    g.store.ReplaceOriginal(f.backup);
    g.selectedGuid = guid;
    AppendLog(L"복구 파일 불러오기 완료: " + f.backup.adapterName + L" (" + guid + L"), 작성 " + f.createdAt + L", 기준 MAC " + (f.backup.macAtBackup ? FormatPlain(*f.backup.macAtBackup) : L"미상"));
    FillList();
    if (MsgBox(L"복구 파일을 불러왔습니다(" + f.backup.adapterName + L"). 지금 원상복구를 실행할까요?", MB_YESNO | MB_ICONQUESTION) == IDYES) {
        // 선택을 해당 어댑터로 이동한 뒤 복구
        for (size_t i = 0; i < g.visible.size(); ++i) if (NameEqualsNoCase(g.adapters[static_cast<size_t>(g.visible[i])].id.interfaceGuid, guid)) { ListView_SetItemState(g.list, static_cast<int>(i), LVIS_SELECTED | LVIS_FOCUSED, LVIS_SELECTED | LVIS_FOCUSED); break; }
        UpdateSelectionInfo();
        DoRestore();
    }
}

void StartRestoreAllThenClose() {
    std::vector<AdapterBackup> baselines;
    for (const auto& guid : g.store.Guids()) baselines.push_back(*g.store.Original(guid));
    AppendLog(L"종료 전 원상복구: 어댑터 " + std::to_wstring(baselines.size()) + L"개");
    if (!StartWorker(Job::RestoreAllThenClose, [baselines] {
            Engine e(g.reg, g.dev, g.sys, g.store);
            bool anyFail = false;
            for (const auto& b : baselines) {
                RestoreRequest rr; rr.identity = b.identity; rr.baseline = b;
                PostLogLine(L"원상복구: " + b.adapterName);
                Report r = e.Restore(rr, [](Phase ph, const std::wstring& m) { PostProgress(ph, m); });
                PostLogLine(FormatReport(r, &Win32ErrorMessage));
                if (!IsRegistryRestored(r.outcome)) anyFail = true;
            }
            auto* d = new DoneMsg{};
            d->job = Job::RestoreAllThenClose; d->restoreAllFailed = anyFail;
            PostMessageW(g.hwnd, WM_APP_DONE, 0, reinterpret_cast<LPARAM>(d));
        })) return;
    SetStatus(L"복구 중…");
    UpdateButtons();
}

void HandleClose() {
    if (g.busy.load()) {
        if (g.currentJob == Job::RestoreAllThenClose) return;
        if (g.currentJob == Job::Refresh) { g.pendingClose = true; return; }   // 조회가 끝나면 종료 절차 계속
        int r = MsgBox(L"작업이 진행 중입니다. 지금 종료하면 어댑터가 중지된 채 남을 수 있어 바로 종료하지 않습니다.\n\n취소를 요청하고, 작업이 안전하게 마무리(보상 복구 포함)되면 종료 절차를 계속할까요?", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2);
        if (r == IDYES) { g.cancel.store(true); g.pendingClose = true; SetStatus(L"취소 요청됨 — 단계 경계에서 중단하고 복구합니다…"); }
        return;
    }
    if (g.store.Count() > 0) {
        std::wstring content = L"이 세션에서 변경한 어댑터 " + std::to_wstring(g.store.Count()) + L"개의 변경 전 상태가 메모리에만 있습니다.\n\n변경을 유지하고 종료하면 다음 실행에서는 전체 원상복구(삭제된 TCP/IP 설정 포함)가 불가능할 수 있습니다. 유지하려면 먼저 '고급 > 복구 파일 내보내기'로 저장하는 것을 권장합니다.";
        int choice = AskExitWithChanges(content);
        if (choice == 0) return;
        if (choice == 1) { StartRestoreAllThenClose(); return; }
    }
    DestroyWindow(g.hwnd);
}

// ------------------------------------------------------------------ 상세 정보 창
LRESULT CALLBACK DetailsProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_SIZE: { HWND e = GetDlgItem(h, 1); if (e) MoveWindow(e, 0, 0, LOWORD(l), HIWORD(l), TRUE); return 0; }
    case WM_CLOSE: DestroyWindow(h); return 0;
    default: return DefWindowProcW(h, m, w, l);
    }
}

void ShowDetails() {
    const AdapterInfo* a = SelectedAdapter();
    if (!a) return;
    static bool registered = false;
    if (!registered) {
        WNDCLASSW wc{};
        wc.lpfnWndProc = DetailsProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"MacChgDetails";
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW); wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        RegisterClassW(&wc);
        registered = true;
    }
    std::wstring title = L"상세 정보 - " + (a->friendlyName.empty() ? a->description : a->friendlyName);
    HWND h = CreateWindowExW(WS_EX_TOOLWINDOW, L"MacChgDetails", title.c_str(), WS_OVERLAPPEDWINDOW | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT, 760, 520, g.hwnd, nullptr, GetModuleHandleW(nullptr), nullptr);
    HWND e = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_VSCROLL | WS_HSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL, 0, 0, 10, 10, h, reinterpret_cast<HMENU>(1), GetModuleHandleW(nullptr), nullptr);
    ApplyFont(e);
    std::wstring text = FormatAdapterDetails(*a);
    const AdapterBackup* b = g.store.Original(a->id.interfaceGuid);
    if (b) {
        text += L"\r\n[이 세션의 변경 전 상태(원상복구 기준)]\r\n";
        text += L"기준 MAC: " + (b->macAtBackup ? FormatPlain(*b->macAtBackup) : std::wstring(L"미상")) + L"\r\n";
        text += L"원래 활성화: " + std::wstring(b->adapterWasEnabled ? L"예" : L"아니오") + L"\r\n";
        text += L"원래 NetworkAddress: " + (b->networkAddressExisted ? RegTypeName(b->networkAddress.type) + L" " + SummarizeRegData(b->networkAddress) : std::wstring(L"없음")) + L"\r\n";
        text += L"TCP/IP 값 " + std::to_wstring(b->tcpipValues.size()) + L"개:\r\n";
        for (const auto& v : b->tcpipValues) text += L"  " + (v.name.empty() ? std::wstring(L"(기본값)") : v.name) + L"  [" + RegTypeName(v.type) + L"]  " + SummarizeRegData(v, 80) + L"\r\n";
    }
    SetWindowTextW(e, text.c_str());
    RECT rc; GetClientRect(h, &rc);
    MoveWindow(e, 0, 0, rc.right, rc.bottom, TRUE);
}

// ------------------------------------------------------------------ 레이아웃
void Layout() {
    RECT rc; GetClientRect(g.hwnd, &rc);
    int W = rc.right, H = rc.bottom;
    int x = 10, y = 8;
    MoveWindow(g.lblList, x, y, 260, 18, TRUE);
    MoveWindow(g.chkShowAll, W - 10 - 330, y - 2, 150, 22, TRUE);
    MoveWindow(g.btnDetails, W - 10 - 175, y - 4, 85, 26, TRUE);
    MoveWindow(g.btnRefresh, W - 10 - 85, y - 4, 85, 26, TRUE);
    y += 24;
    int listH = (H > 560) ? 200 : 160;
    MoveWindow(g.list, x, y, W - 20, listH, TRUE);
    y += listH + 8;
    MoveWindow(g.note, x, y, W - 20, 34, TRUE);
    y += 38;
    MoveWindow(g.lblCur, x, y + 4, 70, 20, TRUE);
    MoveWindow(g.curMac, x + 75, y, 300, 24, TRUE);
    y += 30;
    MoveWindow(g.lblNew, x, y + 4, 70, 20, TRUE);
    MoveWindow(g.newMac, x + 75, y, 150, 24, TRUE);
    MoveWindow(g.newMacColon, x + 230, y + 4, 150, 20, TRUE);
    MoveWindow(g.btnRandom, x + 385, y - 1, 90, 26, TRUE);
    MoveWindow(g.chkFix02, x + 482, y + 1, W - x - 492, 22, TRUE);
    y += 34;
    int bw = 120, bx = x;
    MoveWindow(g.btnChange, bx, y, bw, 30, TRUE); bx += bw + 8;
    MoveWindow(g.btnRandomChange, bx, y, 170, 30, TRUE); bx += 178;
    MoveWindow(g.btnRestore, bx, y, bw, 30, TRUE); bx += bw + 8;
    MoveWindow(g.btnCancel, bx, y, 80, 30, TRUE);
    y += 38;
    MoveWindow(g.status, x, y, W - 20, 20, TRUE);
    y += 24;
    MoveWindow(g.log, x, y, W - 20, H - y - 10, TRUE);
}

HWND MakeCtl(const wchar_t* cls, const wchar_t* text, DWORD style, int id, DWORD exStyle = 0) {
    HWND h = CreateWindowExW(exStyle, cls, text, style | WS_CHILD | WS_VISIBLE, 0, 0, 10, 10, g.hwnd, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)), GetModuleHandleW(nullptr), nullptr);
    ApplyFont(h);
    return h;
}

void CreateControls() {
    g.lblList = MakeCtl(L"STATIC", L"어댑터 (물리 유선랜·무선랜):", 0, 0);
    g.chkShowAll = MakeCtl(L"BUTTON", L"가상·기타 어댑터도 표시", BS_AUTOCHECKBOX | WS_TABSTOP, IDC_SHOWALL);
    g.btnDetails = MakeCtl(L"BUTTON", L"상세 정보", WS_TABSTOP, IDC_DETAILS);
    g.btnRefresh = MakeCtl(L"BUTTON", L"새로 고침", WS_TABSTOP, IDC_REFRESH);
    g.list = MakeCtl(WC_LISTVIEWW, L"", LVS_REPORT | LVS_SINGLESEL | LVS_SHOWSELALWAYS | WS_TABSTOP, IDC_LIST, WS_EX_CLIENTEDGE);
    ListView_SetExtendedListViewStyle(g.list, LVS_EX_FULLROWSELECT | LVS_EX_GRIDLINES);
    const wchar_t* cols[] = {L"종류", L"이름", L"설명", L"연결 상태", L"현재 MAC", L"비고"};
    int widths[] = {90, 150, 260, 110, 140, 140};
    for (int i = 0; i < 6; ++i) {
        LVCOLUMNW c{}; c.mask = LVCF_TEXT | LVCF_WIDTH; c.pszText = const_cast<LPWSTR>(cols[i]); c.cx = widths[i];
        ListView_InsertColumn(g.list, i, &c);
    }
    g.note = MakeCtl(L"STATIC", L"", 0, IDC_NOTE);
    g.lblCur = MakeCtl(L"STATIC", L"현재 MAC:", 0, 0);
    g.curMac = MakeCtl(L"EDIT", L"", ES_READONLY | ES_AUTOHSCROLL, IDC_CURMAC, WS_EX_CLIENTEDGE);
    g.lblNew = MakeCtl(L"STATIC", L"새 MAC:", 0, 0);
    g.newMac = MakeCtl(L"EDIT", L"", ES_AUTOHSCROLL | ES_UPPERCASE | WS_TABSTOP, IDC_NEWMAC, WS_EX_CLIENTEDGE);
    SendMessageW(g.newMac, EM_SETLIMITTEXT, 17, 0);
    g.newMacColon = MakeCtl(L"STATIC", L"", 0, IDC_NEWMAC_COLON);
    g.btnRandom = MakeCtl(L"BUTTON", L"랜덤 생성", WS_TABSTOP, IDC_RANDOM);
    g.chkFix02 = MakeCtl(L"BUTTON", L"첫 바이트 02 고정 (무선랜용 — 모든 무선랜의 변경을 보장하지는 않음)", BS_AUTOCHECKBOX | WS_TABSTOP, IDC_FIX02);
    g.btnChange = MakeCtl(L"BUTTON", L"변경", BS_DEFPUSHBUTTON | WS_TABSTOP, IDC_CHANGE);
    g.btnRandomChange = MakeCtl(L"BUTTON", L"랜덤 생성 후 즉시 변경", WS_TABSTOP, IDC_RANDOM_CHANGE);
    g.btnRestore = MakeCtl(L"BUTTON", L"원상복구", WS_TABSTOP, IDC_RESTORE);
    g.btnCancel = MakeCtl(L"BUTTON", L"취소", WS_TABSTOP, IDC_CANCEL);
    g.status = MakeCtl(L"STATIC", L"상태: 준비", 0, IDC_STATUS);
    g.log = MakeCtl(L"EDIT", L"", ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL | WS_VSCROLL, IDC_LOG, WS_EX_CLIENTEDGE);
    SendMessageW(g.log, EM_SETLIMITTEXT, 0x7FFFFFFE, 0);

    HMENU bar = CreateMenu();
    HMENU file = CreatePopupMenu();
    AppendMenuW(file, MF_STRING, IDM_CLEARLOG, L"결과 지우기(&C)");
    AppendMenuW(file, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(file, MF_STRING, IDM_EXIT, L"종료(&X)");
    HMENU adv = CreatePopupMenu();
    AppendMenuW(adv, MF_STRING, IDM_EXPORT, L"복구 파일 내보내기(&E)…");
    AppendMenuW(adv, MF_STRING, IDM_IMPORT, L"복구 파일 불러오기(&I)…");
    AppendMenuW(adv, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(adv, MF_STRING, IDM_FACTORY, L"제조사 기본 MAC 사용 (NetworkAddress 제거)(&F)…");
    HMENU help = CreatePopupMenu();
    AppendMenuW(help, MF_STRING, IDM_ABOUT, L"정보(&A)");
    AppendMenuW(bar, MF_POPUP, reinterpret_cast<UINT_PTR>(file), L"파일(&F)");
    AppendMenuW(bar, MF_POPUP, reinterpret_cast<UINT_PTR>(adv), L"고급(&A)");
    AppendMenuW(bar, MF_POPUP, reinterpret_cast<UINT_PTR>(help), L"도움말(&H)");
    SetMenu(g.hwnd, bar);
}

void ShowAbout() {
    std::wstring s = L"MacChanger " + std::wstring(kAppVersion) + L" (" + ProcessArchLabel() + L" 빌드)\n\n";
    s += L"Windows용 포터블 MAC 주소 변경 도구 (C++ / Win32).\n";
    s += L"- 설정 파일·레지스트리·AppData·로그·텔레메트리를 만들지 않습니다. 복구 데이터는 메모리에만 있습니다.\n";
    s += L"- '포터블'은 앱 자체의 잔여물이 없다는 뜻입니다. 사용자가 요청한 네트워크 설정 변경은 당연히 시스템에 남고, Windows 가 자체 생성하는 실행·감사 기록까지 없다는 뜻은 아닙니다.\n";
    s += L"- 문서화된 API(IP Helper, SetupAPI, 레지스트리, BCrypt)만 사용합니다. 커널 드라이버·펌웨어 변경·외부 실행 파일을 쓰지 않습니다.\n";
    s += L"- MAC 적용 성공과 인터넷 연결은 별개로 표시합니다.\n";
    MsgBox(s, MB_ICONINFORMATION);
}

void OnDone(DoneMsg* d) {
    std::unique_ptr<DoneMsg> msg(d);
    g.busy.store(false);
    Job job = g.currentJob;
    g.currentJob = Job::None;
    if (job == Job::RestoreAllThenClose) {
        if (msg->restoreAllFailed) {
            SetStatus(L"일부 복구 실패");
            if (MsgBox(L"일부 어댑터의 원상복구가 완료되지 않았습니다. 결과 창의 내용을 확인하세요.\n\n그래도 종료할까요? (종료하면 남은 메모리 백업은 사라집니다. 고급 > 복구 파일 내보내기로 먼저 저장할 수 있습니다)", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) != IDYES) { g.pendingClose = false; UpdateButtons(); RefreshAdapters(); return; }
        }
        DestroyWindow(g.hwnd);
        return;
    }
    const Report& r = msg->report;
    AppendLog(FormatReport(r, &Win32ErrorMessage));
    switch (r.outcome) {
    case Outcome::Success: case Outcome::Restored: SetStatus(L"완료"); break;
    case Outcome::RebootRequired: SetStatus(L"완료(재부팅 필요 — 미확인)"); break;
    case Outcome::AppliedAdapterDisabled: case Outcome::AppliedUnverified: case Outcome::RestoredUnverified: SetStatus(L"완료(미확인)"); break;
    case Outcome::VerifyTimeout: SetStatus(L"확인 불가(시간 초과)"); break;
    case Outcome::RestoredMacMismatch: SetStatus(L"복구 완료(MAC 불일치)"); break;
    case Outcome::NothingChanged: SetStatus(L"중단(변경 없음)"); break;
    case Outcome::DriverIgnoredRolledBack: SetStatus(L"적용 실패(드라이버가 값을 무시) — 복구됨"); break;
    case Outcome::FailedRolledBack: case Outcome::CancelledRolledBack: SetStatus(L"실패/취소 — 직전 상태로 복구됨"); break;
    default: SetStatus(L"실패 — 복구 실패(남은 변경 사항 있음)"); break;
    }
    if (r.outcome == Outcome::FailedRollbackFailed || r.outcome == Outcome::CancelledRollbackFailed || r.outcome == Outcome::DriverIgnoredRollbackFailed) {
        MsgBox(L"작업이 실패했고 직전 상태로 되돌리기도 실패했습니다. 결과 창의 '남은 변경 사항'과 오류 코드를 확인하세요.", MB_ICONERROR);
    }
    if (g.pendingClose) {
        // 취소 후 종료 흐름 계속(새로 고침은 생략)
        g.pendingClose = false;
        UpdateButtons();
        PostMessageW(g.hwnd, WM_CLOSE, 0, 0);
        return;
    }
    UpdateButtons();
    RefreshAdapters();
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_CREATE:
        g.hwnd = hwnd;
        g.font = CreateUiFont();
        CreateControls();
        Layout();
        return 0;
    case WM_SIZE:
        Layout();
        return 0;
    case WM_GETMINMAXINFO: {
        auto* mmi = reinterpret_cast<MINMAXINFO*>(lp);
        mmi->ptMinTrackSize.x = 820; mmi->ptMinTrackSize.y = 560;
        return 0;
    }
    case WM_COMMAND: {
        int id = LOWORD(wp);
        switch (id) {
        case IDC_REFRESH: RefreshAdapters(); break;
        case IDC_SHOWALL: FillList(); break;
        case IDC_DETAILS: ShowDetails(); break;
        case IDC_RANDOM: DoRandom(); break;
        case IDC_CHANGE: if (!g.busy.load()) DoChange(); break;
        case IDC_RANDOM_CHANGE: if (!g.busy.load() && DoRandom()) DoChange(); break;
        case IDC_RESTORE: if (!g.busy.load()) DoRestore(); break;
        case IDC_CANCEL: if (g.busy.load()) { g.cancel.store(true); SetStatus(L"취소 요청됨 — 단계 경계에서 중단하고 복구합니다…"); EnableWindow(g.btnCancel, FALSE); } break;
        case IDC_NEWMAC:
            if (HIWORD(wp) == EN_CHANGE) { Mac m; SetWindowTextW(g.newMacColon, ParseMac(GetText(g.newMac), m) ? (L"(" + FormatColon(m) + L")").c_str() : L""); }
            break;
        case IDM_EXIT: PostMessageW(hwnd, WM_CLOSE, 0, 0); break;
        case IDM_EXPORT: if (!g.busy.load()) DoExport(); break;
        case IDM_IMPORT: if (!g.busy.load()) DoImport(); break;
        case IDM_FACTORY: if (!g.busy.load()) DoFactoryMac(); break;
        case IDM_ABOUT: ShowAbout(); break;
        case IDM_CLEARLOG: SetWindowTextW(g.log, L""); break;
        default: break;
        }
        return 0;
    }
    case WM_NOTIFY: {
        auto* nm = reinterpret_cast<NMHDR*>(lp);
        if (nm->idFrom == IDC_LIST) {
            if (nm->code == LVN_ITEMCHANGED) { auto* lv = reinterpret_cast<NMLISTVIEW*>(lp); if ((lv->uChanged & LVIF_STATE) && (lv->uNewState & LVIS_SELECTED)) UpdateSelectionInfo(); }
            else if (nm->code == NM_DBLCLK) ShowDetails();
        }
        return 0;
    }
    case WM_APP_PROGRESS: {
        std::unique_ptr<std::wstring> s(reinterpret_cast<std::wstring*>(lp));
        Phase ph = static_cast<Phase>(wp);
        SetStatus(DescribePhase(ph) + L" — " + *s);
        AppendLog(DescribePhase(ph) + L": " + *s);
        return 0;
    }
    case WM_APP_LOG: {
        std::unique_ptr<std::wstring> s(reinterpret_cast<std::wstring*>(lp));
        AppendLog(*s);
        return 0;
    }
    case WM_APP_ADAPTERS: {
        std::unique_ptr<std::vector<AdapterInfo>> list(reinterpret_cast<std::vector<AdapterInfo>*>(lp));
        g.adapters = std::move(*list);
        g.busy.store(false);
        g.currentJob = Job::None;
        FillList();
        if (GetText(g.status).find(L"조회 중") != std::wstring::npos) SetStatus(L"준비");
        UpdateButtons();
        if (g.pendingClose) { g.pendingClose = false; PostMessageW(g.hwnd, WM_CLOSE, 0, 0); }
        return 0;
    }
    case WM_APP_DONE:
        OnDone(reinterpret_cast<DoneMsg*>(lp));
        return 0;
    case WM_CLOSE:
        HandleClose();
        return 0;
    case WM_DESTROY:
        if (g.worker) { WaitForSingleObject(g.worker, 30000); CloseHandle(g.worker); g.worker = nullptr; }
        if (g.font) DeleteObject(g.font);
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(hwnd, msg, wp, lp);
    }
}

} // namespace

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, LPWSTR, int) {
    INITCOMMONCONTROLSEX icc{sizeof(icc), ICC_LISTVIEW_CLASSES | ICC_STANDARD_CLASSES};
    InitCommonControlsEx(&icc);

    WNDCLASSW wc{};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInst;
    wc.lpszClassName = L"MacChangerMain";
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_BTNFACE + 1);
    wc.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
    RegisterClassW(&wc);

    HWND hwnd = CreateWindowExW(0, wc.lpszClassName, kAppTitle, WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 900, 640, nullptr, nullptr, hInst, nullptr);
    if (!hwnd) return 1;

    // 권한/아키텍처 확인: 제어 불가 사유를 표시하고 조회 전용으로 동작
    if (!IsProcessElevated()) { g.canControl = false; g.controlBlockReason = L"관리자 권한이 없습니다. 매니페스트의 UAC 승격이 적용되지 않았습니다. 관리자 권한으로 다시 실행하세요."; }
    else if (IsRunningUnderWow64()) { g.canControl = false; g.controlBlockReason = L"32비트(x86) 빌드가 64비트 Windows 에서 실행 중입니다. SetupAPI 장치 제어가 ERROR_IN_WOW64 로 실패하므로 x64 빌드를 사용하세요. 조회만 가능합니다."; }
    else {
        std::wstring native;
        if (IsRunningUnderEmulation(&native)) AppendLog(L"주의: 이 프로세스는 에뮬레이션(네이티브 " + native + L")으로 실행 중입니다. 장치 제어가 실패할 수 있으며, 이 조합은 검증되지 않았습니다.");
    }
    ShowWindow(hwnd, SW_SHOW);
    AppendLog(L"MacChanger " + std::wstring(kAppVersion) + L" (" + ProcessArchLabel() + L"). 이 프로그램은 파일·레지스트리 설정을 만들지 않으며 복구 데이터는 메모리에만 보관합니다.");
    AppendLog(L"변경 중 선택한 어댑터의 네트워크가 잠시 끊깁니다. 작업 중에는 다른 버튼이 잠깁니다.");
    if (!g.canControl) { AppendLog(L"제어 불가: " + g.controlBlockReason); MessageBoxW(hwnd, g.controlBlockReason.c_str(), kAppTitle, MB_ICONWARNING); }
    RefreshAdapters();

    MSG m;
    while (GetMessageW(&m, nullptr, 0, 0) > 0) {
        if (!IsDialogMessageW(hwnd, &m)) { TranslateMessage(&m); DispatchMessageW(&m); }
    }
    return 0;
}
