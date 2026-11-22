@echo off
setlocal
set "HUBATTERY_CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%~dp0dist\" mkdir "%~dp0dist"
"%HUBATTERY_CSC%" /nologo /target:exe /main:LogitechProtocolTests /codepage:65001 ^
    /out:"%~dp0dist\LogitechProtocolTests.exe" /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /recurse:"%~dp0src\*.cs" "%~dp0tests\LogitechProtocolTests.cs"
if errorlevel 1 exit /b %errorlevel%
"%~dp0dist\LogitechProtocolTests.exe"
exit /b %errorlevel%
