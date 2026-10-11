@echo off
setlocal
set "ZHUOBIANJI_DOTNET="
if defined DOTNET_ROOT if exist "%DOTNET_ROOT%\dotnet.exe" set "ZHUOBIANJI_DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not defined ZHUOBIANJI_DOTNET for /f "delims=" %%D in ('where.exe dotnet.exe 2^>nul') do if not defined ZHUOBIANJI_DOTNET set "ZHUOBIANJI_DOTNET=%%D"
if not defined ZHUOBIANJI_DOTNET if exist "F:\dotnet\dotnet.exe" set "ZHUOBIANJI_DOTNET=F:\dotnet\dotnet.exe"
if not defined ZHUOBIANJI_DOTNET (
    echo .NET 10 SDK was not found. Install it or set DOTNET_ROOT to its directory. 1>&2
    exit /b 1
)
for %%D in ("%ZHUOBIANJI_DOTNET%") do set "DOTNET_ROOT=%%~dpD"
set "PATH=%DOTNET_ROOT%;%PATH%"
if /i "%DOTNET_ROOT%"=="F:\dotnet\" (
    if not defined DOTNET_CLI_HOME set "DOTNET_CLI_HOME=F:\dotnet\.cli-home"
    if not defined NUGET_PACKAGES set "NUGET_PACKAGES=F:\dotnet\.nuget\packages"
)
"%ZHUOBIANJI_DOTNET%" %*
exit /b %errorlevel%
