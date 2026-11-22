@echo off
setlocal
set "HUBATTERY_CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "HUBATTERY_OUTPUT=%~dp0dist"
if not "%~1"=="" set "HUBATTERY_OUTPUT=%~f1"
if not exist "%HUBATTERY_CSC%" (
    echo No se encontro el compilador de .NET Framework de 64 bits.
    exit /b 1
)
if not exist "%HUBATTERY_OUTPUT%\" mkdir "%HUBATTERY_OUTPUT%"
"%HUBATTERY_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
    /win32manifest:"%~dp0src\HuBattery.manifest" /out:"%HUBATTERY_OUTPUT%\HuBattery.exe" ^
    /win32icon:"%~dp0assets\HuBattery.ico" ^
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /recurse:"%~dp0src\*.cs"
exit /b %errorlevel%
