@echo off
rem Build bin\seu-auth.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No downloads needed.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found: %CSC%
    exit /b 1
)
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /codepage:65001 /target:exe /platform:anycpu /optimize+ ^
    /out:"%~dp0bin\seu-auth.exe" ^
    /r:System.Core.dll /r:System.Security.dll /r:System.ServiceProcess.dll /r:System.Management.dll ^
    "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
echo Built: %~dp0bin\seu-auth.exe
