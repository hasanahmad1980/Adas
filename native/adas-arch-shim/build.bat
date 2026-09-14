@echo off
rem Builds Adas.Core\Assets\DLSS5\adas-arch-shim.dll (x64). Needs MSVC Build Tools (2019 or 2022).
setlocal
set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSDIR=%%i
if not defined VSDIR set VSDIR=C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cd /d "%~dp0"
cl /nologo /O2 /MT /LD /EHsc /W3 /DUNICODE /D_UNICODE adas-arch-shim.cpp /link /DLL /OUT:..\..\Adas.Core\Assets\DLSS5\adas-arch-shim.dll || exit /b 1
del /q *.obj *.exp *.lib 2>nul
