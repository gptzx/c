# MacChanger — Windows용 MAC 주소 변경 포터블 유틸리티

C# WinForms, **.NET Framework 4.0** 대상, **단일 EXE**, 완전 포터블.
설정 파일(ini/json)과 프로그램 자체의 레지스트리 키(HKCU 등)를 **만들지 않습니다**. .NET Settings/user.config도 사용하지 않습니다.
Windows 7(.NET 4.0 설치됨) / 8 / 8.1 / 10 / 11에서 추가 런타임 설치 없이 실행됩니다. (Windows 8 이상은 .NET 4.5+가 OS에 내장되어 있어 바로 실행됩니다. Windows 7은 Windows Update로 배포된 .NET 4.x가 설치되어 있어야 합니다.)

## 기능

| 기능 | 구현 |
|---|---|
| 어댑터 열거 | `root\StandardCimv2\MSFT_NetAdapter` → 실패/빈 결과 시 `Win32_NetworkAdapter` → `GetAdaptersAddresses` 순으로 폴백. 드롭다운에 `[유선]/[무선]/[블루투스]/[기타]` 라벨 + 설명 + 현재 MAC 표시 (Windows 7의 `Win32_NetworkAdapter` 경로에서는 `GetAdaptersAddresses`의 IfType으로 무선 여부를 보강) |
| 원래(공장) MAC | `\\.\{GUID}`에 `IOCTL_NDIS_QUERY_GLOBAL_STATS` + `OID_802_3_PERMANENT_ADDRESS`로 조회, 실패 시 열거 시점에 읽어 둔 `MSFT_NetAdapter.PermanentAddress`. 파일에 저장하지 않고 매번 조회 |
| 현재 MAC | `OID_802_3_CURRENT_ADDRESS` → `GetAdaptersAddresses` → WMI 순으로 조회 |
| 랜덤 MAC 주소 | 선택한 어댑터 종류에 따라 자동. **무선**: 12자리 중 왼쪽에서 두 번째 자리만 `2/6/A/E` 중 하나(`X2/X6/XA/XE-XX-XX-XX-XX-XX`), 나머지 11자리는 `0~F` 전부 무작위(첫 자리도 고정하지 않음). **유선/기타**: 두 번째 자리만 짝수(`0/2/4/6/8/A/C/E`), 나머지 11자리 `0~F`, `00-00-00-00-00-00`과 `FF-FF-FF-FF-FF-FF` 제외 |
| 변경 적용 | 확인 대화 상자 없이 바로 실행. (a) SetupAPI `DICS_DISABLE`(실패 시 WMI `Disable()`) → (b) `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-…}\00XX` 중 `NetCfgInstanceId`가 일치하는 키에 `NetworkAddress`(REG_SZ, 하이픈 없는 12자리) 기록 → (c) Tcpip 값 자동 정리(아래 참고) → (d) SetupAPI `DICS_ENABLE`(실패 시 WMI `Enable()` + 재시도) → NDIS에서 현재 MAC을 다시 읽어 검증 |
| Tcpip 값 자동 정리 | `Tcpip\Parameters\Interfaces\{GUID}`의 `EnableDHCP`가 **1**이면 그 키의 값을 `EnableDHCP`만 남기고 모두 삭제(하위 키는 유지)하고, 전역 `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters`의 `DhcpDomain`·`DhcpNameServer` 값도 삭제. `EnableDHCP`가 **0**(고정 IP)이거나 값이 없으면 아무것도 지우지 않음. 변경 적용·원상복구·IP 갱신 모두에서 수행 |
| IP 갱신 | 확인 대화 상자 없이 바로 실행. MAC은 그대로 두고(`NetworkAddress` 유지) 어댑터 비활성화 → Tcpip 값 자동 정리 → 활성화로 IP만 새로 받음 (공장 MAC 상태에서 원상복구가 하던 일과 같음) |
| 원상복구 | 확인 대화 상자 없이 바로 실행. `NetworkAddress` 값 삭제 → Tcpip 값 자동 정리 → 어댑터 재시작 → 공장 MAC과 비교. 현재 MAC이 이미 공장 MAC이고 `NetworkAddress` 값도 없으면 "복구할 내용이 없습니다" 안내만 표시 |
| 재부팅 보류 처리 | 장치 관리자가 어댑터를 즉시 중지/재시작하지 못해 재부팅 필요 플래그(`DI_NEEDREBOOT`)를 설정하면, 잘못된 "드라이버 거부" 판정 대신 "재부팅 후 적용" 상태로 안내 |
| 현재 IP 실시간 표시 | 선택한 어댑터의 IPv4 주소, 서브넷 마스크, 기본 게이트웨이, DNS 서버(기본·보조 모두)를 **IP 확인 주기**마다 다시 읽어 표시 (어댑터 비활성 / 링크 없음 / 할당 대기 / 169.254.x.x 자동 사설 주소 구분) |
| IP 확인 주기 | IP 구성을 다시 읽는 간격을 밀리초 단위로 직접 설정 (기본 **1000ms**, 바꾸면 바로 적용). 위/아래 버튼(키보드 화살표·마우스 휠 포함)은 100ms 단위로 100~2000ms 안에서만 움직이고, 직접 입력하면 1~2000ms 어떤 값이든 됨. 현재 IP 표시·할당 IP 로그·자동 변경의 IP 감지가 모두 이 주기로 동작. 자동 변경이 실행 중일 때는 잠기며 정지한 뒤에만 바꿀 수 있음 |
| 자동 변경 | **IP 할당 후 [N]초 뒤 새 MAC 적용**을 체크하고 **시작**을 누르면 선택한 어댑터에 IP가 할당될 때마다(같은 IP를 다시 받아도) N초(0~604800 = 최대 7일, 기본 10; 0이면 1초 이내에 바로) 뒤에 어댑터 종류에 맞는 랜덤 MAC을 확인 창 없이 적용하고, 재시작 후 IP를 받으면 다시 반복. 같은 버튼(**정지**)을 다시 누르면 멈춤. 시작할 때 이미 IP가 있으면 바로 세기 시작하며, 남은 시간은 버튼에 "정지 (N초)"(1시간 이상이면 "H:MM:SS", 하루 이상이면 "D일 H:MM:SS")로 1초마다 갱신해 표시. 실행 중에는 **정지**와 **로그 지우기** 버튼만 쓸 수 있고 나머지 버튼·체크박스("IP 할당 후" 포함)·어댑터 선택·지연 시간·IP 확인 주기는 모두 잠김(정지한 뒤에만 바꿀 수 있음). 변경이 실패하거나 재부팅이 필요하면 스스로 정지 |
| 송수신 그래프 | 창 아래쪽에 TMAC(Technitium MAC Address Changer) 식 그래프: 흰 바탕에 **수신(빨강)**·**송신(초록)** 속도 꺾은선(1초 샘플 = 가로 2픽셀로 TMAC 과 같은 밀도, 최신 값이 오른쪽 끝, 보이는 구간의 최대값을 위 끝에 맞춤)과 그 아래 **수신 / --속도 / 송신 / --속도** 네 줄(`23.75 KB (24315 바이트)`, `6.05 KB/s (6193 바이트)` 형식, 1024 단위). 선택한 어댑터의 64비트 카운터(`GetIfEntry2`)를 1초마다 읽어 계산. 누적량은 어댑터를 선택한 시점부터 세며, **MAC 변경·원상복구·IP 갱신이 끝나면 그래프는 그대로 두고 누적량만 0부터 다시 셈**(TMAC 과 같은 동작). 다른 어댑터를 고르면 그래프도 새로 시작. 로그 상자가 보일 때는 아래쪽을 반으로 나눠 왼쪽 로그 상자·오른쪽 그래프, 로그 상자를 숨기면 그래프가 전체 폭을 사용 |
| 할당 IP 로그 | 선택한 어댑터에 새 IP가 할당될 때마다 로그 상자에 한 줄(`[시각(탭)]IP[(탭)서브넷 마스크][(탭)기본 게이트웨이][(탭)MAC]`)을 추가. **시간**·**서브넷 마스크**·**기본 게이트웨이**·**MAC** 항목은 각각 체크박스로 켜고 끌 수 있고(기본 꺼짐; 게이트웨이가 없으면 `(없음)`), **로그 상자 표시**로 상자를 보이거나 숨길 수 있음(기본 숨김, 숨기면 그래프가 그 자리까지 차지. 숨겨진 동안에는 상자에 기록하지 않으며 켤 때 현재 IP가 마지막 줄과 다를 때만 한 줄 추가). **로그 지우기**로 상자 내용을 비움. 169.254.x.x 자동 사설 주소는 로그에 넣지 않으며, DHCP 전환 중 169.254 주소가 잠깐 붙었다 떨어져도 같은 IP를 다시 기록하지 않음. 작업 중에는 기록하지 않고 작업 완료 후 새 MAC과 함께 기록 |
| 할당 IP 로그 저장 | 체크하면 로그 상자와 같은 줄을 실행 파일 옆 `MacChanger-ip.log`에도 추가 (기본 꺼짐, 켜지 않으면 어떤 파일도 만들지 않음). 켜는 순간 현재 할당된 IP를 기준 줄로 한 번 기록 |
| 상태 표시 | 상태 라벨: `준비 / 진행 / 완료 / 실패` + 진행 단계 메시지 (전체 내용은 라벨 툴팁) |
| 백그라운드 실행 | **백그라운드 실행** 버튼을 누르면 창을 숨기고 트레이(알림 영역) 아이콘만 남긴 채 계속 실행 (자동 변경·그래프·로그 모두 그대로 동작). 트레이 아이콘 두 번 클릭 또는 오른쪽 클릭 → **열기**로 창을 다시 열고, **종료**로 프로그램을 끝냄. 숨겨진 동안 실패/재부팅 안내가 필요하면 창을 먼저 다시 연 뒤 안내 창을 띄움 |
| 아이콘 | 실행 파일에 아이콘이 내장되어 있고(16·32 BMP + 256 PNG), 창/작업표시줄 아이콘도 EXE 리소스에서 같은 아이콘을 읽어 사용 (UNC 경로에서도 동작) |

## 호환 / 권한

* `app.manifest`에 `requestedExecutionLevel level="requireAdministrator"`가 포함되어 UAC 승격을 요구합니다.
* 레지스트리는 항상 `RegistryView.Registry64`로 열어 64비트 OS에서 32비트 프로세스로 실행되어도 WOW64 리다이렉션 문제가 없습니다. (32비트 OS에서는 Registry64 지정이 무시됩니다.)
* 빌드는 **AnyCPU(Prefer32Bit=false)**이므로 64비트 OS에서는 64비트 프로세스로 실행됩니다. 32비트 프로세스로 실행되면 `SetupDiCallClassInstaller`가 `ERROR_IN_WOW64`로 실패하는데, 이 경우 자동으로 WMI 폴백을 사용합니다.
* 무선 어댑터는 드라이버/OS 제약으로 두 번째 자리가 `2/6/A/E`(로컬 관리 주소)가 아니면 변경이 무시될 수 있습니다. 랜덤 생성이 무선 어댑터에서 이 규칙을 자동으로 따릅니다. 적용 후 현재 MAC이 요청값과 다르면 안내 메시지를 띄웁니다. Windows 10 이상의 Wi-Fi "임의 하드웨어 주소" 설정이 켜져 있으면(`HKLM\SOFTWARE\Microsoft\WlanSvc\Interfaces\{GUID}\RandomMacState` 또는 `%ProgramData%\Microsoft\Wlansvc\Profiles\Interfaces\{GUID}\*.xml`의 `<enableRandomization>true</enableRandomization>`) 경고합니다.

## 파일 구성

```
MacChanger.sln
build.cmd                              ← Visual Studio 없이 빌드 (in-box csc.exe)
.gitattributes                         ← build.cmd / .sln은 CRLF로 체크아웃
MacChanger/
  MacChanger.csproj                    ← .NET Framework 4.0, AnyCPU, 매니페스트·아이콘 포함
  app.manifest                         ← requireAdministrator, supportedOS(Win7~11), dpiAware
  app.ico                              ← 실행 파일/창 아이콘 (16·32·256)
  Program.cs                           ← 진입점, 관리자 권한 확인, 전역 예외 처리
  MainForm.cs / MainForm.Designer.cs   ← UI (드롭다운, 원래/현재 MAC, 현재 IP·서브넷 마스크·기본 게이트웨이·DNS, 새 MAC, 랜덤 MAC 주소, 변경 적용, 원상복구, IP 갱신, 로그 지우기, 백그라운드 실행(트레이), 자동 변경(지연 초, 시작/정지), IP 확인 주기(ms), 로그 옵션 6개, 상태 라벨, 할당 IP 로그 상자, 송수신 그래프)
  TrafficGraph.cs                      ← TMAC 식 송수신 그래프 컨트롤 (수신 빨강 / 송신 초록 꺾은선 + 누적량·속도 네 줄)
  IntervalUpDown.cs                    ← IP 확인 주기 입력 칸 (버튼은 100ms 단위·100 이상, 직접 입력은 1ms 단위)
  Properties/AssemblyInfo.cs
  Core/
    MacAddressUtil.cs                  ← MAC 정규화/서식/검증/랜덤 생성(무선·유선 규칙)
    NetworkAdapterInfo.cs              ← 어댑터 정보 모델, 유선/무선/블루투스 라벨
    AdapterEnumerator.cs               ← MSFT_NetAdapter → Win32_NetworkAdapter → GetAdaptersAddresses(NetworkInterface) 열거
    IpMonitor.cs                       ← 현재 IPv4 구성(주소·마스크·게이트웨이·DNS) 조회, 169.254 제외(주소·마스크), 로그 줄 만들기(IP·마스크·게이트웨이·MAC), 로그 파일 기록
    TrafficMonitor.cs                  ← 송수신 바이트 카운터 읽기(GetIfEntry2), 속도·누적량 계산, MAC 변경·원상복구·IP 갱신 시 누적량 초기화, KB/MB 서식
    NdisQuery.cs                       ← IOCTL_NDIS_QUERY_GLOBAL_STATS로 공장/현재 MAC 조회
    MacRegistry.cs                     ← HKLM(64비트 뷰) NetworkAddress / EnableDHCP 확인 / Tcpip 값 정리(인터페이스 + 전역 DhcpDomain·DhcpNameServer) / 임의 하드웨어 주소 설정 확인
    AdapterController.cs               ← SetupAPI DICS_DISABLE/ENABLE(재부팅 필요 플래그 보고), WMI Enable()/Disable() 폴백
    MacChangeService.cs                ← 변경 적용 / 원상복구 / IP 갱신 절차
  Native/
    NativeMethods.cs                   ← kernel32 / user32 / iphlpapi(ConvertInterfaceGuidToLuid·GetIfEntry2) / setupapi P/Invoke
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
2. 드롭다운에서 어댑터 선택 → 원래(공장) MAC / 현재 MAC / 현재 IP·서브넷 마스크·기본 게이트웨이·DNS가 표시됩니다. IP 구성은 **IP 확인 주기**(기본 1000ms)마다 갱신되며, 밀리초 단위로 바꿀 수 있습니다.
3. **랜덤 MAC 주소**를 누르거나 새 MAC을 직접 입력합니다. (`02-1A-2B-3C-4D-5E`, `021A2B3C4D5E`, `02:1A:…` 모두 허용) 랜덤 생성은 선택한 어댑터가 무선이면 두 번째 자리를 2/6/A/E로, 무선이 아니면(유선/블루투스/기타) 짝수로 만듭니다.
4. **변경 적용** → 확인 창 없이 바로 어댑터가 재시작되며, 완료 후 현재 MAC과 IP가 갱신됩니다. 진행 단계와 결과는 상태 라벨에 표시됩니다.
5. **원상복구** → 확인 창 없이 바로 `NetworkAddress` 값을 지우고(필요 시 Tcpip 값 정리) 어댑터를 재시작하여 공장 MAC으로 돌아갑니다. 현재 MAC이 이미 공장 MAC이고 레지스트리에 `NetworkAddress` 값도 없으면 "복구할 내용이 없습니다" 안내만 하고 아무것도 하지 않습니다. (자동 변경 실행 중에는 잠겨 있으니 먼저 **정지**를 누르세요.)
6. **IP 갱신** → MAC은 바꾸지 않고 어댑터를 재시작(필요 시 Tcpip 값 정리)하여 IP만 새로 받습니다. 현재 MAC이 공장 MAC이든 바꾼 MAC이든 그대로 유지됩니다.
7. **자동 변경** → "IP 할당 후"를 체크하고 초(0이면 즉시)를 정한 뒤 **시작**을 누르면 IP가 할당될 때마다 그 시간 뒤에 새 MAC이 자동으로 적용되고 계속 반복됩니다. 멈추려면 같은 버튼(**정지**)을 누르세요. 실행 중에는 정지와 로그 지우기 외의 조작이 잠깁니다. 로그 항목(시간·서브넷 마스크·기본 게이트웨이·MAC·상자·파일)은 그대로 IP만 기록합니다.
8. **백그라운드 실행** → 창을 숨기고 트레이 아이콘으로 계속 실행합니다. 자동 변경 중에도 누를 수 있으며, 트레이 아이콘을 두 번 클릭하거나 오른쪽 클릭 → 열기로 돌아옵니다.
9. **송수신 그래프** → 창 아래쪽 그래프가 선택한 어댑터의 수신(빨강)·송신(초록) 속도를 1초마다 그리고, 아래 네 줄에 누적량과 현재 속도를 보여 줍니다. MAC 변경·원상복구·IP 갱신이 끝나면 그래프는 유지되고 누적량만 0부터 다시 셉니다. **로그 상자 표시**를 켜면 아래쪽이 왼쪽 로그 상자 / 오른쪽 그래프로 나뉩니다.

## 주의 사항

* **Tcpip 값 자동 정리**는 `EnableDHCP = 1`(DHCP 사용)인 어댑터에서만 동작하며, 그 어댑터의 `Tcpip\Parameters\Interfaces\{GUID}` 키에서 `EnableDHCP`를 제외한 **모든 값**이 삭제됩니다. DHCP 임대 정보(`DhcpIPAddress`, `DhcpNameServer`, `DhcpDomain` 등)뿐 아니라 수동으로 지정한 DNS(`NameServer`), `Domain`, `InterfaceMetric`, `MTU` 등 이 키에 저장된 설정도 함께 지워지므로, DHCP 어댑터에 DNS 등을 직접 설정해 두었다면 변경/복구/IP 갱신 후 다시 설정하세요. 전역 `Tcpip\Parameters`의 `DhcpDomain`·`DhcpNameServer`도 삭제됩니다. 고정 IP(`EnableDHCP = 0`) 어댑터, `EnableDHCP` 값이 없는 어댑터, 그리고 어댑터를 즉시 중지하지 못한 경우(재부팅 보류)에는 아무 값도 지우지 않습니다.
* 변경 적용, 원상복구, IP 갱신은 확인 창 없이 즉시 실행됩니다. 실패하거나 재부팅이 필요한 경우에만 안내 창이 뜹니다.
* **자동 변경**은 정지하기 전까지 계속 반복됩니다(IP 할당 → N초 대기 → MAC 변경 → 어댑터 재시작 → 새 IP 할당 → …). 네트워크 연결이 주기적으로 끊기므로 필요할 때만 시작하세요. 설정은 저장되지 않습니다.
* **할당 IP 로그 저장**을 켜면 실행 파일과 같은 폴더에 `MacChanger-ip.log`가 만들어집니다(이 옵션을 켤 때만). 로그 옵션(시간·서브넷 마스크·기본 게이트웨이·MAC·로그 상자 표시·파일 저장)은 모두 기본 꺼짐이고 저장되지 않으므로 실행할 때마다 필요하면 다시 설정하세요.
* 일부 드라이버(특히 무선, 일부 USB 이더넷)는 `NetworkAddress` 값을 지원하지 않거나 로컬 관리 주소(두 번째 자리 2/6/A/E)만 허용합니다.
* 어댑터를 즉시 중지할 수 없는 경우(장치 관리자가 재부팅 필요 플래그 설정) 레지스트리 값은 기록되고 재부팅 후 적용됩니다. 프로그램이 이를 감지해 "재부팅 후 적용" 안내를 띄웁니다.
* 프로그램 자체는 어떤 설정 파일/레지스트리 키도 만들지 않습니다. 다만 .NET 런타임이 일부 Windows 10 버전에서 `%LOCALAPPDATA%\Microsoft\CLR_v4.0\UsageLogs\`에 사용 로그를 남기는 것은 OS/런타임 동작으로 프로그램과 무관합니다.
