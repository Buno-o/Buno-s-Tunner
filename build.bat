@echo off
setlocal enabledelayedexpansion
set PAUSECMD=pause
if /i "%CI%"=="true" set PAUSECMD=rem
cd /d "%~dp0"
set PLAT=x64
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  set PLAT=x86
  set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
)
if not exist "%CSC%" (
  echo Nao encontrei o compilador do .NET Framework 4.x. Instale o .NET Framework 4.8 e tente de novo.
  %PAUSECMD% & exit /b 1
)
if not exist libs\LibreHardwareMonitorLib.dll (
  echo Copie TODAS as .dll do LibreHardwareMonitor para a pasta libs\ e rode de novo. Veja LEIA-ME.txt
  %PAUSECMD% & exit /b 1
)
set ICON=
if exist BunosTunner.ico set ICON=/win32icon:BunosTunner.ico
set RES=
for %%f in (libs\*.dll) do set RES=!RES! /resource:%%f,%%~nxf
echo Compilando para %PLAT% ...
"%CSC%" /nologo /target:winexe /platform:%PLAT% /optimize+ /codepage:65001 /out:BunosTunner.exe /win32manifest:app.manifest !ICON! /lib:libs /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Management.dll /r:Microsoft.VisualBasic.dll /r:LibreHardwareMonitorLib.dll !RES! BunosTunner.cs
if errorlevel 1 (
  echo.
  echo ERRO de compilacao - copie as mensagens acima e me mande.
  %PAUSECMD% & exit /b 1
)
echo.
echo Pronto: BunosTunner.exe (portable, arquivo unico). Execute como Administrador.
%PAUSECMD%
