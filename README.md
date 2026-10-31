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
| 랜덤 MAC 주소 | 선택한 어댑터 종류에 따라 자동. **무선**: 12자리 중 왼쪽에서 두 번째 자리만 `2/6/A/E` 중 하나(`X2/X6/XA/XE-XX-XX-XX-XX-XX`), 나머지 11자리는 `0~F` 전부 무작위(첫 자리도 고정하지 않음). **유선/기타**: 두 번째 자리만 짝수(`0/2/4/6/8/A/C/E`), 나머지 11자리 `0~F`, `00-00-00-00-00-00` 제외(`FF-FF-FF-FF-FF-FF`는 짝수 규칙으로 이미 나오지 않음) |
| 변경 적용 | 확인 대화 상자 없이 바로 실행. (a) `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-…}\00XX` 중 `NetCfgInstanceId`가 일치하는 키에 `NetworkAddress`(REG_SZ, 하이픈 없는 12자리) 기록 → (b) Tcpip 값 자동 정리(아래 참고) → (c) 어댑터 재시작 → NDIS에서 현재 MAC을 다시 읽어 검증 |
| 어댑터 재시작 방식 | 랜카드 속성 창(고급 탭)에서 값을 바꾸고 확인을 눌렀을 때와 같은 SetupAPI `DIF_PROPERTYCHANGE` + `DICS_PROPCHANGE` 한 번(devcon `restart`와 동일). "사용 안 함" 상태를 거치지 않고 장치를 멈췄다가 바로 다시 시작하므로 네트워크 아이콘이 잠깐 사라졌다 나타나는 정도로 끝나고, 예전의 비활성화 → 활성화 두 단계보다 빠름. 이미 "사용 안 함"인 어댑터는 재시작 대신 활성화(`DICS_ENABLE`)해서 올림. SetupAPI 호출 자체가 안 되는 환경에서만 예전 방식(`DICS_DISABLE` → `DICS_ENABLE`, 실패 시 WMI `Disable()`/`Enable()`)으로 대신함 |
| Tcpip 값 자동 정리 | `Tcpip\Parameters\Interfaces\{GUID}`의 `EnableDHCP`가 **1**이면 그 키의 값을 정리(Wi-Fi 의 SSID 별 임대 캐시 하위 키도 삭제, 그 밖의 하위 키는 유지): `NameServer`(직접 지정한 DNS)에 값이 있으면 `EnableDHCP`와 `NameServer`를 남기고, `NameServer`가 비어 있거나 없으면 `EnableDHCP`만 남기고 나머지를 모두 삭제. **DNS 초기화**를 체크하면 `NameServer`에 값이 있어도 `EnableDHCP`만 남김. 전역 `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters`의 `DhcpDomain`·`DhcpNameServer` 값도 삭제. `EnableDHCP`가 **0**(고정 IP)이거나 값이 없으면 아무것도 지우지 않음. 변경 적용·원상복구·IP 갱신 모두에서 수행 |
| IP 갱신 | 확인 대화 상자 없이 바로 실행. MAC은 그대로 두고(`NetworkAddress` 유지) Tcpip 값 자동 정리 → 어댑터 재시작으로 IP만 새로 받음 (공장 MAC 상태에서 원상복구가 하던 일과 같음) |
| IP 반환 | `ipconfig /release`와 같은 동작을 외부 명령 없이 직접 수행: iphlpapi `GetInterfaceInfo`로 어댑터의 `\DEVICE\TCPIP_{GUID}` 항목을 찾아 `IpReleaseAddress`를 호출하면 DHCP 클라이언트가 서버에 DHCPRELEASE를 보내고 주소를 내림. **즉시 반환** 버튼은 누르는 즉시 반환하고(어댑터는 그대로, 새 IP는 IP 갱신이나 어댑터 재시작 때 받음), **DHCP 서버에 IP 반환**을 체크하면 변경 적용·IP 갱신·원상복구(자동 변경 포함)가 `[1/4] DHCP 서버에 IP 반환` 단계를 먼저 거침. 고정 IP(`EnableDHCP = 0`)이거나 할당된 IP가 없으면 건너뛰고, 작업 중의 반환 실패는 경고로만 남기고 계속 진행 |
| 원상복구 | 네 작업 가운데 유일하게 확인 대화 상자(현재 MAC → 공장 MAC, 예/아니요)를 거침. `NetworkAddress` 값 삭제 → Tcpip 값 자동 정리 → 어댑터 재시작 → 공장 MAC과 비교. 현재 MAC이 이미 공장 MAC이고 `NetworkAddress` 값도 없으면 "복구할 내용이 없습니다" 안내만 표시 |
| 재부팅 보류 처리 | PnP 관리자가 어댑터를 즉시 재시작하지 못해(드라이버가 중지를 거부) 재부팅 필요 플래그(`DI_NEEDREBOOT`)를 설정하면, 잘못된 "드라이버 거부" 판정 대신 "재부팅 후 적용" 상태로 안내 |
| 현재 IP 실시간 표시 | 선택한 어댑터의 IPv4 주소, 서브넷 마스크, 기본 게이트웨이, DNS 서버(기본·보조 모두)를 Windows 가 IP 변화를 알려 줄 때마다 즉시 다시 읽어 표시 (어댑터 비활성 / 링크 없음 / 할당 대기 / 169.254.x.x 자동 사설 주소 구분). 표시 기준은 `ipconfig`와 같음: 랜선이 빠지는 등 링크가 없으면 남아 있는 주소를 보이지 않고, 실제 IP를 받았으면 함께 붙어 있던 169.254 주소는 보이지 않음 |
| IP 변화 감지 | 주기 폴링 대신 Windows 의 IP 변경 알림을 받음: `NotifyIpInterfaceChange`(링크 연결/끊김, 어댑터 추가/제거), `NotifyUnicastIpAddressChange`(IPv4 주소 추가/삭제/상태 변화), `NotifyRouteChange2`(기본 게이트웨이 변화)에 콜백을 등록(Vista 이상)해 변화가 있을 때만 어댑터 구성을 다시 읽음 — 대기 중 CPU 소모 0, DHCP 가 주소를 붙이는 순간 밀리초 단위로 반응. 몰려오는 알림은 한 번의 갱신으로 합치고, 1초 뒤 한 번 더 읽어 주소보다 늦게 기록되는 게이트웨이·DNS 를 반영하며, 놓친 변화를 메우는 안전망으로 3초마다 한 번 더 읽음. 중복 주소 감지에서 중복(Duplicate)·무효(Invalid)로 판정된 주소는 표시·로그·자동 변경에서 제외. DNS 서버만 바뀌는 경우(주소·경로 변화 없음)는 알림이 없으므로 3초 안전망에서 반영됨. 알림을 등록할 수 없는 OS 에서는 1초 폴링으로 자동 전환. IP 조회 자체는 작업자 스레드에서 하므로(어댑터 전환 중에는 수백 ms 걸릴 수 있음) 그래프·카운트다운·할당 대기 시계를 돌리는 1초 타이머가 밀리지 않고, 그래프는 전용 타이머가 돌리며 어디서도 재시작하지 않아 정확히 1초 간격으로 갱신됨(카운트다운 타이머만 예약 시각에 맞춰 재시작). 현재 IP 표시·할당 IP 로그·자동 변경의 IP 감지·할당 대기 판정이 모두 이 경로로 동작하며 사용자가 맞출 주기는 없음 |
| 자동 변경 | **사용**을 체크하고 **시작**을 누르면 토글 스위치로 고른 방식대로 어댑터 종류에 맞는 랜덤 MAC을 확인 창 없이 반복 적용. 스위치 왼쪽 **IP 할당 시**: 선택한 어댑터에 IP가 할당될 때마다(같은 IP를 다시 받아도) "IP 할당 후 [N]초 뒤 새 MAC 적용"의 N초(0~604800 = 최대 7일, 기본 10; 0이면 IP 가 보이는 즉시) 뒤에 적용하고, 재시작 후 IP를 받으면 다시 반복. 스위치 오른쪽 **일정 시간마다**: IP 할당 여부와 관계없이 "IP와 관계없이 [N]초마다 새 MAC 적용"의 N초(10~604800 = 10초~7일, 기본 60)마다 적용 — 시작하면 N초 뒤에 첫 변경, 그 뒤로는 변경이 끝난 시점부터 다시 N초를 셈(변경이 겹치지 않고 어댑터가 N초 동안은 그대로 동작). 두 방식 모두 같은 버튼(**정지**)을 다시 누르면 멈춤. 남은 시간은 버튼에 "정지 (N초)"(1시간 이상이면 "H:MM:SS", 하루 이상이면 "D일 H:MM:SS")로 1초마다 갱신해 표시(IP 할당 시 방식은 시작할 때 이미 IP가 있으면 바로 세기 시작). 스위치 양쪽 글자(IP 할당 시 / 일정 시간마다)를 눌러도 방식이 바뀌고, 고르지 않은 방식의 설정 줄은 흐리게 표시. 실행 중에는 **정지**·**로그 비우기**·**백그라운드 실행** 버튼만 쓸 수 있고 나머지 버튼·체크박스("사용" 포함)·방식 스위치·어댑터 선택·지연 시간·할당 대기 초·간격 초·자동 정지·DNS 초기화·IP 반환(옵션·즉시 반환 버튼)은 모두 잠김(정지한 뒤에만 바꿀 수 있음). 변경이 실패하거나 재부팅이 필요하면 스스로 정지. **자동 정지**: "자동 정지: 지정 IP/대역 할당 시 정지"를 체크하면 입력 칸이 열리고(체크를 풀면 입력한 값은 지워지고 칸이 잠김), IP 또는 대역을 적어 두면 자동 변경 중 그에 속하는 IP가 할당되는 순간 자동 변경을 스스로 정지. `123` → 123.0.0.0~123.255.255.255(/8), `123.123` → /16, `123.123.123` → /24, `123.123.123.123` → 그 주소만. 시작할 때나 MAC을 바꾸기 직전에 이미 해당 IP가 할당되어 있으면 바꾸지 않고 바로 정지하며, IP 줄은 평소처럼 기록. 옥텟은 10진수로 읽음(`192.168.001.010` = `192.168.1.10`); 형식이 틀리면 시작하지 않음. **할당 대기 초과 시 새 MAC**(IP 할당 시 방식): 자동 변경 중 어댑터는 올라왔는데 실제 IP를 못 받는 상태(할당 대기 중 / 169.254 자동 사설 주소만 있음)가 "IP 할당 대기가 [N]초를 넘으면 새 MAC 적용"의 N초(10~60, 기본 30)를 넘기면 새 랜덤 MAC을 적용하고, IP를 받을 때까지 반복(별도 체크 없이 IP 할당 시 방식에 항상 포함). 대기한 시간은 버튼에 "정지 (IP 대기 N초)"로 표시. 일정 시간마다 방식에서는 IP 가 사라져도 예약이 취소되지 않고 할당 대기도 세지 않음(지정 IP 자동 정지는 두 방식 모두 적용). 지연 초·할당 대기 초(IP 할당 시)와 간격 초(일정 시간마다)는 "사용"을 체크하고 그 방식을 골랐을 때만 열림 |
| 송수신 그래프 | 창 아래쪽에 TMAC(Technitium MAC Address Changer) 식 그래프: 흰 바탕에 **수신(빨강)**·**송신(초록)** 속도 꺾은선(1초 샘플 = 가로 2픽셀로 TMAC 과 같은 밀도, 최신 값이 오른쪽 끝, 보이는 구간의 최대값을 위 끝에 맞춤)과 그 아래 **수신 / --속도 / 송신 / --속도** 네 줄(`23.75 KB (24315 바이트)`, `6.05 KB/s (6193 바이트)` 형식, 1024 단위). 선택한 어댑터의 64비트 카운터(`GetIfEntry2`)를 1초마다 읽어 계산. 누적량은 어댑터를 선택한 시점부터 세며, **MAC 변경·원상복구·IP 갱신이 끝나면 그래프는 그대로 두고 누적량만 0부터 다시 셈**(TMAC 과 같은 동작). 다른 어댑터를 고르면 그래프도 새로 시작. 그래프 영역(262×167)은 가로·세로 비율도 TMAC 과 같음(꺾은선 영역 약 2.6 : 1 — 창 폭은 그대로 두고 세로만 맞춤). 아래쪽은 왼쪽 로그 상자·오른쪽 그래프로 반씩 나뉨 |
| 할당 IP 로그 | 선택한 어댑터에 새 IP가 할당될 때마다 로그 상자에 한 줄(`[시각(탭)]IP[(탭)서브넷 마스크][(탭)기본 게이트웨이][(탭)MAC]`)을 추가. **시간**·**서브넷 마스크**·**기본 게이트웨이**·**MAC** 항목은 각각 체크박스로 켜고 끌 수 있고(기본 꺼짐; 게이트웨이가 없으면 `(없음)`), **로그 상자 사용**으로 상자를 켜고 끌 수 있음(기본 꺼짐; 상자는 늘 보이되 꺼져 있으면 흐리게 표시되고 기록하지 않으며, 켤 때 현재 IP가 마지막 줄과 다를 때만 한 줄 추가). **로그 비우기**는 켜짐/꺼짐과 상관없이 언제나 상자 내용을 비움. 169.254.x.x 자동 사설 주소는 로그에 넣지 않으며, DHCP 전환 중 169.254 주소가 잠깐 붙었다 떨어져도 같은 IP를 다시 기록하지 않음. 작업 중에는 기록하지 않고 작업 완료 후 새 MAC과 함께 기록 |
| 할당 IP 로그 저장 | 체크하면 로그 상자와 같은 줄을 실행 파일 옆 `MacChanger-ip.log`에도 추가 (기본 꺼짐, 켜지 않으면 어떤 파일도 만들지 않음). 켜는 순간 현재 할당된 IP를 기준 줄로 한 번 기록 |
| 상태 표시 | 상태 라벨: `준비 / 진행 / 완료 / 실패` + 짧은 결과 메시지. 라벨에 다 들어가지 않아 잘려 보일 때만 툴팁에 전체 문장 |
| 백그라운드 실행 | **백그라운드 실행** 버튼을 누르면 창을 숨기고 트레이(알림 영역) 아이콘만 남긴 채 계속 실행 (자동 변경·그래프·로그 모두 그대로 동작). 트레이 아이콘을 한 번 클릭하면 창이 다시 열리고, 오른쪽 클릭 메뉴의 **열기** / **종료**로도 열거나 끝낼 수 있음. 이 메뉴는 기본 메뉴 대신 직접 그림: 왼쪽 아이콘 여백 없이 모서리가 살짝 둥근 밝은 판과 그 둘레로 번지는 부드러운 그림자(아우라), 마우스를 올린 항목은 옅게 강조. 숨겨진 동안 실패/재부팅 안내가 필요하면 창을 먼저 다시 연 뒤 안내 창을 띄움 |
| 아이콘 | 실행 파일에 아이콘이 내장되어 있고(16·32 BMP + 256 PNG), 창/작업표시줄 아이콘도 EXE 리소스에서 같은 아이콘을 읽어 사용 (UNC 경로에서도 동작) |

## 호환 / 권한

* `app.manifest`에 `requestedExecutionLevel level="requireAdministrator"`가 포함되어 UAC 승격을 요구합니다.
* 레지스트리는 항상 `RegistryView.Registry64`로 열어 64비트 OS에서 32비트 프로세스로 실행되어도 WOW64 리다이렉션 문제가 없습니다. (32비트 OS에서는 Registry64 지정이 무시됩니다.)
* 빌드는 **AnyCPU(Prefer32Bit=false)**이므로 64비트 OS에서는 64비트 프로세스로 실행됩니다. 32비트 프로세스로 실행되면 `SetupDiCallClassInstaller`가 `ERROR_IN_WOW64`로 실패하는데, 이 경우 `DICS_PROPCHANGE` 재시작 대신 예전 방식(비활성화 → 활성화, 각각 WMI `Disable()`/`Enable()` 폴백)을 자동으로 사용합니다.
* 무선 어댑터는 드라이버/OS 제약으로 두 번째 자리가 `2/6/A/E`(로컬 관리 주소)가 아니면 변경이 무시될 수 있습니다. 랜덤 생성이 무선 어댑터에서 이 규칙을 자동으로 따릅니다. 적용 후 현재 MAC이 요청값과 다르면 안내 메시지를 띄웁니다. Windows 10 이상의 Wi-Fi "임의 하드웨어 주소" 설정이 켜져 있으면(`HKLM\SOFTWARE\Microsoft\WlanSvc\Interfaces\{GUID}\RandomMacState` 또는 `%ProgramData%\Microsoft\Wlansvc\Profiles\Interfaces\{GUID}\*.xml`의 `<enableRandomization>true</enableRandomization>`) 경고합니다.

## 파일 구성

```
MacChanger.sln
build.cmd                              ← Visual Studio 없이 빌드 (in-box csc.exe)
tools/Shrink.cs                        ← dist/ 용 실행 파일 축소 도구 (Mono.Cecil: 매개변수 이름·비공개 이름·무효 특성 제거, 기능 동일)
.gitattributes                         ← build.cmd / .sln은 CRLF로 체크아웃
MacChanger/
  MacChanger.csproj                    ← .NET Framework 4.0, AnyCPU, 매니페스트·아이콘 포함
  app.manifest                         ← requireAdministrator, supportedOS(Win7~11), dpiAware
  app.ico                              ← 실행 파일/창 아이콘 (16·32·256)
  Program.cs                           ← 진입점, 관리자 권한 확인, 전역 예외 처리
  MainForm.cs                          ← UI 동작 (드롭다운, 원래/현재 MAC, 현재 IP·서브넷 마스크·기본 게이트웨이·DNS, 새 MAC, 랜덤 MAC 주소, DNS 초기화, 변경 적용, IP 갱신, 원상복구, 로그 비우기, 백그라운드 실행(트레이), 자동 변경(방식 스위치: IP 할당 시(지연 초, 할당 대기 초 10~60) / 일정 시간마다(간격 10초~7일), 시작/정지), 지정 IP/대역 할당 시 자동 정지, IP 반환(옵션·즉시 반환 버튼), 로그 옵션 6개, IP 변화 알림 수신, 상태 라벨, 할당 IP 로그 상자, 송수신 그래프)
  MainForm.Designer.cs                 ← 컨트롤 생성·배치 (손으로 관리: 위치·크기 표 하나를 루프로 적용, 표 순서 = 탭 순서; Visual Studio 디자이너용 파일이 아님)
  ModeSwitch.cs                        ← 자동 변경 방식 토글 스위치 (둥근 홈 위의 손잡이, 클릭·Space·Enter·←/→)
  TrafficGraph.cs                      ← TMAC 식 송수신 그래프 컨트롤 (수신 빨강 / 송신 초록 꺾은선 + 누적량·속도 네 줄)
  TrayMenu.cs                          ← 트레이 팝업 메뉴 (레이어드 창에 둥근 판·테두리·아우라 그림자·항목을 직접 그림)
  Properties/AssemblyInfo.cs
  Core/
    MacAddressUtil.cs                  ← MAC 정규화/서식/검증/랜덤 생성(무선·유선 규칙)
    NetworkAdapterInfo.cs              ← 어댑터 정보 모델, 유선/무선/블루투스 라벨
    AdapterEnumerator.cs               ← MSFT_NetAdapter → Win32_NetworkAdapter → GetAdaptersAddresses(NetworkInterface) 열거
    DhcpRelease.cs                     ← DHCP 서버에 IP 반환 (GetInterfaceInfo + IpReleaseAddress, ipconfig /release 와 같음)
    IpChangeNotifier.cs                ← Windows IP 변경 알림(NotifyIpInterfaceChange / NotifyUnicastIpAddressChange / NotifyRouteChange2) 등록·해제
    IpMonitor.cs                       ← 현재 IPv4 구성(주소·마스크·게이트웨이·DNS) 조회, 169.254 제외(주소·마스크), 로그 줄 만들기(IP·마스크·게이트웨이·MAC), 로그 파일 기록
    TrafficMonitor.cs                  ← 송수신 바이트 카운터 읽기(GetIfEntry2), 속도·누적량 계산, MAC 변경·원상복구·IP 갱신 시 누적량 초기화, KB/MB 서식
    NdisQuery.cs                       ← IOCTL_NDIS_QUERY_GLOBAL_STATS로 공장/현재 MAC 조회
    MacRegistry.cs                     ← HKLM(64비트 뷰) NetworkAddress / EnableDHCP 확인 / Tcpip 값 정리(인터페이스 + 전역 DhcpDomain·DhcpNameServer) / 임의 하드웨어 주소 설정 확인
    AdapterController.cs               ← SetupAPI DICS_PROPCHANGE 재시작(재부팅 필요 플래그 보고), 폴백 DICS_DISABLE/ENABLE + WMI Enable()/Disable()
    MacChangeService.cs                ← 변경 적용 / 원상복구 / IP 갱신 / IP 반환 절차 (작업 전 IP 반환 단계 포함)
  Native/
    NativeMethods.cs                   ← kernel32 / user32 / iphlpapi(ConvertInterfaceGuidToLuid·GetIfEntry2·NotifyIpInterfaceChange·NotifyUnicastIpAddressChange·NotifyRouteChange2·CancelMibChangeNotify2·GetInterfaceInfo·IpReleaseAddress) / setupapi P/Invoke
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

### dist/ 실행 파일 축소 (선택)

`dist/MacChanger.exe`는 위 빌드 결과를 `tools/Shrink.cs`(Mono.Cecil 0.11)로 한 번 더 거친 것입니다. 이 단계는 IL·리소스(아이콘·매니페스트·버전 정보)·동작을
건드리지 않고 메타데이터만 줄입니다: 매개변수 이름 제거, 이 프로그램이 정의한 형식·메서드·필드·속성·이벤트의 이름을 한두 글자로(생성자·P/Invoke·가상/재정의
메서드·델리게이트의 런타임 메서드만 그대로 — 런타임이 이름으로 찾는 것은 이것뿐), 컴파일 때 호출 자리에 새겨져 실행 중에는 읽히지 않는 상수 필드(const·열거형 멤버)
제거, 실행에 영향 없는 특성(컴파일러·디버거 표식, 어셈블리 제목·설명 특성 — 파일 속성 창의 정보는 Win32 버전 리소스에 따로 있어 그대로 보임) 제거. 이 프로그램은
리플렉션·`Enum.ToString()`·형식 이름을 쓰지 않으므로 동작이 같습니다. 같은 입력이면 같은 출력이 나오며(타임스탬프 보존), 세 번째 인수로 이름표 파일을 주면
원래 이름 → 새 이름 대응을 적어 두어 테스트가 축소된 파일에서도 그대로 돕니다. `build.cmd`로 직접 빌드한 EXE 는 이름만 길 뿐 기능이 같습니다. 아이콘(`app.ico`)의 세 프레임은 모두 PNG(zopfli)로 압축되어 있습니다 (Windows Vista 이상에서 지원).

```
mcs -r:Mono.Cecil.dll -r:System.Core -out:Shrink.exe tools/Shrink.cs
mono Shrink.exe out/MacChanger.exe dist/MacChanger.exe [names.txt]
```

## 사용 방법

1. `MacChanger.exe` 실행 → UAC 승격 확인.
2. 드롭다운에서 어댑터 선택 → 원래(공장) MAC / 현재 MAC / 현재 IP·서브넷 마스크·기본 게이트웨이·DNS가 표시됩니다. IP 구성은 Windows 가 변화를 알려 주는 즉시 갱신됩니다.
3. **랜덤 MAC 주소**를 누르거나 새 MAC을 직접 입력합니다. (`02-1A-2B-3C-4D-5E`, `021A2B3C4D5E`, `02:1A:…` 모두 허용) 랜덤 생성은 선택한 어댑터가 무선이면 두 번째 자리를 2/6/A/E로, 무선이 아니면(유선/블루투스/기타) 짝수로 만듭니다.
4. **변경 적용** → 확인 창 없이 바로 어댑터가 재시작되며, 완료 후 현재 MAC과 IP가 갱신됩니다. 진행 단계와 결과는 상태 라벨에 표시됩니다.
5. **원상복구** → 확인 창에서 **예**를 누르면 `NetworkAddress` 값을 지우고(필요 시 Tcpip 값 정리) 어댑터를 재시작하여 공장 MAC으로 돌아갑니다. 현재 MAC이 이미 공장 MAC이고 레지스트리에 `NetworkAddress` 값도 없으면 "복구할 내용이 없습니다" 안내만 하고 아무것도 하지 않습니다. (자동 변경 실행 중에는 잠겨 있으니 먼저 **정지**를 누르세요.)
6. **IP 갱신** → MAC은 바꾸지 않고 어댑터를 재시작(필요 시 Tcpip 값 정리)하여 IP만 새로 받습니다. 현재 MAC이 공장 MAC이든 바꾼 MAC이든 그대로 유지됩니다.
7. **IP 반환** → **즉시 반환**을 누르면 `ipconfig /release`처럼 현재 IP를 DHCP 서버에 돌려주고 어댑터는 IP 없이 남습니다(새 IP는 IP 갱신이나 어댑터 재시작 때). **DHCP 서버에 IP 반환**을 체크해 두면 변경 적용·IP 갱신·원상복구와 자동 변경의 매 변경이 먼저 IP를 반환한 뒤 진행됩니다.
8. **자동 변경** → (원하면 "자동 정지"의 "지정 IP/대역 할당 시 정지"를 체크하고 멈출 IP나 대역을 적은 뒤) **사용**을 체크하고 토글 스위치로 방식을 고릅니다. 왼쪽 **IP 할당 시**: 지연 초(0이면 즉시)와 할당 대기 초(10~60)를 정한 뒤 **시작**을 누르면 IP가 할당될 때마다 그 시간 뒤에 새 MAC이 자동으로 적용되고, IP를 못 받은 채 할당 대기 초를 넘겨도 새 MAC이 적용되며 계속 반복됩니다. 오른쪽 **일정 시간마다**: 간격(10초~7일)을 정한 뒤 **시작**을 누르면 IP 할당과 관계없이 그 간격마다 새 MAC이 적용됩니다(변경이 끝난 뒤부터 다시 셉니다). 멈추려면 같은 버튼(**정지**)을 누르세요. 실행 중에는 정지·로그 비우기·백그라운드 실행 외의 조작이 잠깁니다. 로그 항목(시간·서브넷 마스크·기본 게이트웨이·MAC·상자·파일)은 그대로 IP만 기록합니다.
9. **백그라운드 실행** → 창을 숨기고 트레이 아이콘으로 계속 실행합니다. 자동 변경 중에도 누를 수 있으며, 트레이 아이콘을 한 번 클릭하거나 오른쪽 클릭 → 열기로 돌아옵니다.
10. **송수신 그래프** → 창 아래쪽 그래프가 선택한 어댑터의 수신(빨강)·송신(초록) 속도를 1초마다 그리고, 아래 네 줄에 누적량과 현재 속도를 보여 줍니다. MAC 변경·원상복구·IP 갱신이 끝나면 그래프는 유지되고 누적량만 0부터 다시 셉니다. 아래쪽은 왼쪽 로그 상자 / 오른쪽 그래프로 나뉘어 있고, **로그 상자 사용**을 켜야 상자에 기록됩니다.

## 주의 사항

* **Tcpip 값 자동 정리**는 `EnableDHCP = 1`(DHCP 사용)인 어댑터에서만 동작하며, 그 어댑터의 `Tcpip\Parameters\Interfaces\{GUID}` 키에서 `EnableDHCP`(그리고 값이 있는 `NameServer`)를 제외한 값이 삭제됩니다. 직접 지정한 DNS(`NameServer`)는 값이 있으면 보존되지만, DHCP 임대 정보(`DhcpIPAddress`, `DhcpNameServer`, `DhcpDomain` 등)와 `Domain`, `InterfaceMetric`, `MTU` 등 이 키의 다른 설정은 함께 지워지므로 직접 설정해 두었다면 변경/복구/IP 갱신 후 다시 설정하세요. **DNS 초기화**를 켜면 직접 지정한 DNS도 지워집니다. 전역 `Tcpip\Parameters`의 `DhcpDomain`·`DhcpNameServer`도 삭제됩니다. 고정 IP(`EnableDHCP = 0`) 어댑터와 `EnableDHCP` 값이 없는 어댑터에서는 아무 값도 지우지 않습니다. 정리는 어댑터 재시작 직전에 이루어지며(장치가 다시 올라올 때 DHCP 클라이언트가 이 키를 새로 읽어 임대 값이 없으면 DHCPDISCOVER 부터 시작), 재시작이 재부팅 뒤로 보류되더라도 지워진 임대 값은 다음 임대 때 다시 채워집니다. 이 키의 하위 키 가운데 Wi-Fi 가 SSID 별로 두는 DHCP 임대 캐시(SSID 를 16진수로 적은 이름의 하위 키로, `DhcpIPAddress`·`DhcpServer`·`Lease` 등 임대 값 전체를 복사해 둠)도 함께 삭제해 같은 SSID 에 다시 붙을 때 옛 임대가 되살아나지 않게 하고, 그 밖의 하위 키는 건드리지 않습니다. Wi-Fi 는 네트워크를 떠나는 순간(재시작으로 인터페이스가 내려갈 때) 현재 SSID 의 캐시를 쓰므로 재시작 직후에 한 번 더 지웁니다.
* **IP 반환**을 켜고 MAC을 바꾸면 옛 MAC의 임대가 서버에서 바로 풀리므로, 서버에 따라 새 MAC이 방금 반환한 그 IP를 다시 받을 수도 있습니다. 반환하지 않으면 옛 임대가 만료될 때까지 옛 MAC에 묶여 있어 다른 IP를 받을 가능성이 큽니다. 반환은 IPv4 DHCP 어댑터에서만 의미가 있고, 관리자 권한이 필요합니다(이 프로그램은 항상 관리자 권한으로 실행).
* 변경 적용·IP 갱신·IP 반환은 확인 창 없이 즉시 실행되고, 원상복구만 확인 창(예/아니요)을 거칩니다. 실패하거나 재부팅이 필요한 경우에는 네 작업 모두 안내 창이 뜹니다.
* **자동 변경**은 정지하기 전까지 계속 반복됩니다(IP 할당 시: IP 할당 → N초 대기 → MAC 변경 → 어댑터 재시작 → 새 IP 할당 → …; 일정 시간마다: N초 대기 → MAC 변경 → 어댑터 재시작 → 다시 N초 대기 → …). 네트워크 연결이 주기적으로 끊기므로 필요할 때만 시작하세요. 설정은 저장되지 않습니다.
* **할당 IP 로그 저장**을 켜면 실행 파일과 같은 폴더에 `MacChanger-ip.log`가 만들어집니다(이 옵션을 켤 때만). 로그 옵션(시간·서브넷 마스크·기본 게이트웨이·MAC·로그 상자 사용·파일 저장)은 모두 기본 꺼짐이고 저장되지 않으므로 실행할 때마다 필요하면 다시 설정하세요.
* 일부 드라이버(특히 무선, 일부 USB 이더넷)는 `NetworkAddress` 값을 지원하지 않거나 로컬 관리 주소(두 번째 자리 2/6/A/E)만 허용합니다.
* 어댑터를 즉시 중지할 수 없는 경우(장치 관리자가 재부팅 필요 플래그 설정) 레지스트리 값은 기록되고 재부팅 후 적용됩니다. 프로그램이 이를 감지해 "재부팅 후 적용" 안내를 띄웁니다.
* 프로그램 자체는 어떤 설정 파일/레지스트리 키도 만들지 않습니다. 다만 .NET 런타임이 일부 Windows 10 버전에서 `%LOCALAPPDATA%\Microsoft\CLR_v4.0\UsageLogs\`에 사용 로그를 남기는 것은 OS/런타임 동작으로 프로그램과 무관합니다.
