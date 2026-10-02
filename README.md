# MacChanger — Windows용 MAC 주소 변경 포터블 유틸리티

C# WinForms, **.NET Framework 4.0** 대상, **단일 EXE**, 완전 포터블.
설정 파일(ini/json)과 프로그램 자체의 레지스트리 키(HKCU 등)를 **만들지 않습니다**. .NET Settings/user.config도 사용하지 않습니다.
Windows 7(.NET 4.0 설치됨) / 8 / 8.1 / 10 / 11에서 추가 런타임 설치 없이 실행됩니다. (Windows 8 이상은 .NET 4.5+가 OS에 내장되어 있어 바로 실행됩니다. Windows 7은 Windows Update로 배포된 .NET 4.x가 설치되어 있어야 합니다.)

## 기능

| 기능 | 구현 |
|---|---|
| 어댑터 열거 | `root\StandardCimv2\MSFT_NetAdapter` → 실패/빈 결과 시 `Win32_NetworkAdapter` → `GetAdaptersAddresses` 순으로 폴백. 드롭다운에 `[유선]/[무선]/[블루투스]/[기타]` 라벨 + 설명 + 현재 MAC 표시 (Windows 7의 `Win32_NetworkAdapter` 경로에서는 `GetAdaptersAddresses`의 IfType으로 무선 여부를 보강) |
| 원래(공장) MAC | `\\.\{GUID}`에 `IOCTL_NDIS_QUERY_GLOBAL_STATS` + `OID_802_3_PERMANENT_ADDRESS`로 조회, 실패 시 `MSFT_NetAdapter.PermanentAddress`. 파일에 저장하지 않고 매번 조회 |
| 현재 MAC | `OID_802_3_CURRENT_ADDRESS` → `GetAdaptersAddresses` → WMI 순으로 조회 |
| 랜덤 생성 | 첫 옥텟 `02` 고정(기본) 또는 `02/06/0A/0E` 중 무작위 → 항상 유니캐스트(bit0=0)·로컬관리(bit1=1). 왼쪽에서 두 번째 자리는 항상 2/6/A/E |
| 변경 적용 | (a) SetupAPI `DICS_DISABLE`(실패 시 WMI `Disable()`) → (b) `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-…}\00XX` 중 `NetCfgInstanceId`가 일치하는 키에 `NetworkAddress`(REG_SZ, 하이픈 없는 12자리) 기록 → (c) `Tcpip\Parameters\Interfaces\{GUID}`에서 `EnableDHCP`만 남기고 모든 값 삭제(하위 키는 유지, 체크박스로 끌 수 있음) → (d) SetupAPI `DICS_ENABLE`(실패 시 WMI `Enable()` + 재시도) → NDIS에서 현재 MAC을 다시 읽어 검증 |
| 원상복구 | `NetworkAddress` 값 삭제 후 어댑터 재시작 → 공장 MAC과 비교 |
| 재부팅 보류 처리 | 장치 관리자가 어댑터를 즉시 중지/재시작하지 못해 재부팅 필요 플래그(`DI_NEEDREBOOT`)를 설정하면, 잘못된 "드라이버 거부" 판정 대신 "재부팅 후 적용" 상태로 안내 |
| 상태 표시 | 상태 라벨: `준비 / 진행 / 완료 / 실패` + 단계별 한국어 로그 |

## 호환 / 권한

* `app.manifest`에 `requestedExecutionLevel level="requireAdministrator"`가 포함되어 UAC 승격을 요구합니다.
* 레지스트리는 항상 `RegistryView.Registry64`로 열어 64비트 OS에서 32비트 프로세스로 실행되어도 WOW64 리다이렉션 문제가 없습니다. (32비트 OS에서는 Registry64 지정이 무시됩니다.)
* 빌드는 **AnyCPU(Prefer32Bit=false)**이므로 64비트 OS에서는 64비트 프로세스로 실행됩니다. 32비트 프로세스로 실행되면 `SetupDiCallClassInstaller`가 `ERROR_IN_WOW64`로 실패하는데, 이 경우 자동으로 WMI 폴백을 사용합니다.
* 무선 어댑터는 드라이버/OS 제약으로 첫 옥텟이 `02`가 아니면 변경이 무시될 수 있습니다. 적용 후 현재 MAC이 요청값과 다르면 안내 메시지를 띄웁니다. Windows 10 이상의 Wi-Fi "임의 하드웨어 주소" 설정이 켜져 있으면(`HKLM\SOFTWARE\Microsoft\WlanSvc\Interfaces\{GUID}\RandomMacState` 또는 `%ProgramData%\Microsoft\Wlansvc\Profiles\Interfaces\{GUID}\*.xml`의 `<enableRandomization>true</enableRandomization>`) 경고합니다.

## 파일 구성

```
MacChanger.sln
build.cmd                              ← Visual Studio 없이 빌드 (in-box csc.exe)
.gitattributes                         ← build.cmd / .sln은 CRLF로 체크아웃
MacChanger/
  MacChanger.csproj                    ← .NET Framework 4.0, AnyCPU, 매니페스트 포함
  app.manifest                         ← requireAdministrator, supportedOS(Win7~11), dpiAware
  Program.cs                           ← 진입점, 관리자 권한 확인, 전역 예외 처리
  MainForm.cs / MainForm.Designer.cs   ← UI (드롭다운, 원래/현재 MAC, 새 MAC, 랜덤 생성, 변경 적용, 원상복구, 상태 라벨, 체크박스 2개, 로그)
  Properties/AssemblyInfo.cs
  Core/
    MacAddressUtil.cs                  ← MAC 정규화/서식/검증/랜덤 생성
    NetworkAdapterInfo.cs              ← 어댑터 정보 모델, 유선/무선/블루투스 라벨
    AdapterEnumerator.cs               ← MSFT_NetAdapter → Win32_NetworkAdapter → GetAdaptersAddresses 열거
    NdisQuery.cs                       ← IOCTL_NDIS_QUERY_GLOBAL_STATS로 공장/현재 MAC 조회
    MacRegistry.cs                     ← HKLM(64비트 뷰) NetworkAddress / Tcpip 값 정리 / 임의 하드웨어 주소 설정 확인
    AdapterController.cs               ← SetupAPI DICS_DISABLE/ENABLE(재부팅 필요 플래그 보고), WMI Enable()/Disable() 폴백
    MacChangeService.cs                ← 변경 적용 / 원상복구 절차
  Native/
    NativeMethods.cs                   ← kernel32 / iphlpapi / setupapi P/Invoke
```

## 빌드 방법

### 방법 A — Visual Studio 없이 (`build.cmd`, 권장)

.NET Framework 4.x가 설치된 Windows라면 어디서나 빌드됩니다.

```bat
build.cmd
```

→ `out\MacChanger.exe` 생성 (AnyCPU, 매니페스트 내장, 단일 파일). 이 파일 하나만 복사하면 됩니다.
소스 폴더를 명시적으로 지정하므로(재귀 검색 아님) Visual Studio가 `MacChanger\obj`에 만들어 두는 생성 파일과 충돌하지 않습니다.

### 방법 B — Visual Studio 2010 ~ 2019 (2022는 아래 3번 참고)

1. `MacChanger.sln` 열기 (VS 2012 이상에서는 솔루션 업그레이드 안내가 나올 수 있으며 그대로 진행하면 됩니다).
2. 구성 `Release | Any CPU` 선택 후 빌드 → `MacChanger\bin\Release\MacChanger.exe`.
3. VS 2017/2019에서 ".NET Framework 4 대상 팩이 없음" 오류가 나면 Visual Studio Installer → 개별 구성 요소 → **.NET Framework 4 targeting pack**을 설치하세요.
   **VS 2022 이상에는 이 구성 요소가 없습니다**(VS 2022는 .NET Framework 4.0~4.5.1 대상 빌드를 지원하지 않음). VS 2022 사용자는
   (1) 방법 A(build.cmd) 또는 방법 C를 사용하거나,
   (2) Microsoft에서 배포하는 **.NET Framework 4 Multi-Targeting Pack**(또는 VS 2019 Build Tools의 ".NET Framework 4 targeting pack")을 설치해 참조 어셈블리(`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0`)를 공급하면 VS 2022에서도 그대로 빌드되며,
   (3) 또는 csproj의 `<ItemGroup>`에 `<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net40" Version="1.0.3" PrivateAssets="all" />`를 추가해 NuGet 복원으로 참조 어셈블리를 받을 수도 있습니다(이 방법은 VS 2017 이상 전용이며 방법 C의 내장 MSBuild 4.0에서는 동작하지 않습니다).

### 방법 C — 내장 MSBuild 명령줄 (Visual Studio 없이)

```bat
%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe MacChanger\MacChanger.csproj /p:Configuration=Release
```

→ `MacChanger\bin\Release\MacChanger.exe`. (32비트/64비트 Windows 공통 경로이며, 64비트에서는 `Framework64\v4.0.30319\MSBuild.exe`를 써도 됩니다.)
VS나 .NET Framework 4 Multi-Targeting Pack이 없는 PC에서는 `warning MSB3644: ".NETFramework,Version=v4.0" 참조 어셈블리를 찾을 수 없습니다` 경고가 한 줄 나오지만, 내장 MSBuild(4.x 툴셋)는 GAC의 런타임 어셈블리로 계속 빌드하므로 EXE는 정상 생성됩니다(방법 A와 같은 어셈블리를 참조). 이 경고는 무시해도 됩니다.
VS 2017 이상의 MSBuild(Developer Command Prompt의 `msbuild`)에서는 같은 상황이 **오류** MSB3644가 되므로, 그 경우에는 방법 B 3번처럼 대상 팩을 설치하거나 방법 A를 사용하세요.

## 사용 방법

1. `MacChanger.exe` 실행 → UAC 승격 확인.
2. 드롭다운에서 어댑터 선택 → 원래(공장) MAC / 현재 MAC / 레지스트리 NetworkAddress 상태가 표시됩니다.
3. **랜덤 생성**을 누르거나 새 MAC을 직접 입력합니다. (`02-1A-2B-3C-4D-5E`, `021A2B3C4D5E`, `02:1A:…` 모두 허용)
4. **변경 적용** → 확인 창의 경고를 읽고 진행. 어댑터가 재시작되며 완료 후 현재 MAC이 갱신됩니다.
5. **원상복구** → `NetworkAddress` 값을 지우고 어댑터를 재시작하여 공장 MAC으로 돌아갑니다.

## 주의 사항

* **Tcpip 값 정리** 체크박스가 켜져 있으면 해당 어댑터의 고정 IP / 서브넷 / 게이트웨이 / DNS 설정이 삭제됩니다(`EnableDHCP`만 유지). 고정 IP를 쓰는 어댑터라면 체크를 끄거나 변경 후 다시 설정하세요.
* 일부 드라이버(특히 무선, 일부 USB 이더넷)는 `NetworkAddress` 값을 지원하지 않거나 로컬 관리 주소(두 번째 자리 2/6/A/E)만 허용합니다.
* 어댑터를 즉시 중지할 수 없는 경우(장치 관리자가 재부팅 필요 플래그 설정) 레지스트리 값은 기록되고 재부팅 후 적용됩니다. 프로그램이 이를 감지해 "재부팅 후 적용" 안내를 띄웁니다.
* 프로그램 자체는 어떤 설정 파일/레지스트리 키도 만들지 않습니다. 다만 .NET 런타임이 일부 Windows 10 버전에서 `%LOCALAPPDATA%\Microsoft\CLR_v4.0\UsageLogs\`에 사용 로그를 남기는 것은 OS/런타임 동작으로 프로그램과 무관합니다.
