@echo off
setlocal
set "HUBATTERY_CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%HUBATTERY_CSC%" (
    echo No se encontro el compilador de .NET Framework de 64 bits.
    exit /b 1
)
if not exist "%~dp0dist\" mkdir "%~dp0dist"
"%HUBATTERY_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
    /win32manifest:"%~dp0src\HuBattery.manifest" /out:"%~dp0dist\HuBattery.exe" ^
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /recurse:"%~dp0src\*.cs"
exit /b %errorlevel%
