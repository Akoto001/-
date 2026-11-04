@echo off
rem Rebuild the Android APK. Keep this file ASCII only: cmd.exe reads batch files
rem in the OEM code page, so Chinese text here would turn into garbage.
setlocal
set "HERE=%~dp0"
set "WS=%HERE%.."
pushd "%HERE%"

set "DOTNET_CLI_HOME=%WS%\.dotnet8-home"
set "NUGET_PACKAGES=%WS%\.dotnet8-home\packages"
set "NUGET_HTTP_CACHE_PATH=%WS%\.dotnet8-home\nuget-http"
set "TEMP=%WS%\.dotnet8-home\tmp"
set "TMP=%TEMP%"
set "JAVA_HOME=C:\Program Files\Java\jdk-17"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

if not exist "%WS%\.dotnet8\dotnet.exe" goto :nodotnet
if not exist "%WS%\.android-sdk\platforms" goto :nosdk

"%WS%\.dotnet8\dotnet.exe" build GeoViewerAndroid.csproj -c Release -f net8.0-android ^
  -p:AndroidSdkDirectory="%WS%\.android-sdk" ^
  -p:JavaSdkDirectory="%JAVA_HOME%" ^
  -p:AppSettingsDirectory="%WS%\.dotnet8-home\appdata" ^
  -p:AndroidKeyStore=true ^
  -p:AndroidSigningKeyStore="%HERE%geo-release.keystore" ^
  -p:AndroidSigningKeyAlias=geo ^
  -p:AndroidSigningKeyPass=geoviewer ^
  -p:AndroidSigningStorePass=geoviewer ^
  -p:PublishTrimmed=false ^
  -p:RunAOTCompilation=false ^
  -p:AndroidEnableMarshalMethods=false

if errorlevel 1 goto :failed

copy /y "bin\Release\net8.0-android\com.georef.geometryviewer-Signed.apk" "%WS%\GeometryViewer-Android.apk" >nul
if errorlevel 1 goto :failed
echo.
echo OK -^> %WS%\GeometryViewer-Android.apk
popd
exit /b 0

:failed
echo.
echo BUILD FAILED - see the messages above.
popd
pause
exit /b 1

:nodotnet
echo.
echo Local .NET SDK not found at "%WS%\.dotnet8\dotnet.exe".
popd
pause
exit /b 1

:nosdk
echo.
echo Android SDK not found at "%WS%\.android-sdk".
popd
pause
exit /b 1
