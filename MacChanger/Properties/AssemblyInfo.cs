using System.Reflection;

// 이 특성들은 컴파일 때 Win32 버전 리소스(파일 속성 창·UAC 창의 프로그램 이름)를 만드는 재료이므로 소스에 남겨 둔다 — dist 축소 단계는 리소스를 만든 뒤의 메타데이터만 뗀다.

[assembly: AssemblyTitle("MAC 주소 변경 유틸리티")]
[assembly: AssemblyDescription("Windows 네트워크 어댑터 MAC 주소 변경 포터블 유틸리티")]
[assembly: AssemblyProduct("MacChanger")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
