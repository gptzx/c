@echo off
rem ============================================================================
rem  MacChanger - build with the in-box .NET Framework 4.x C# compiler.
rem  No Visual Studio required. Works on Windows 7 (.NET 4.0 installed) ~ 11.
rem  Output: out\MacChanger.exe  (single portable EXE, AnyCPU, manifest embedded)
rem ============================================================================
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] .NET Framework 4.x csc.exe not found. Install .NET Framework 4.0 or later.
    exit /b 1
)

if not exist out mkdir out
if exist out\MacChanger.exe del /q out\MacChanger.exe

echo Using compiler: %CSC%
rem Source folders are listed explicitly (no /recurse) so that files MSBuild/Visual Studio
rem generate under MacChanger\obj are never picked up.
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /warn:4 /codepage:65001 ^
  /out:out\MacChanger.exe ^
  /win32manifest:MacChanger\app.manifest ^
  /win32icon:MacChanger\app.ico ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Management.dll ^
  MacChanger\*.cs MacChanger\Properties\*.cs MacChanger\Core\*.cs MacChanger\Native\*.cs
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)
echo.
echo [OK] Built out\MacChanger.exe
endlocal
