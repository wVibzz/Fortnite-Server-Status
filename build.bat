@echo off
REM Builds FortniteStatus.exe using the C# compiler that ships with .NET Framework (no install needed).
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REF=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8

echo Fortnite Status build
echo.
echo Compiler: %CSC%
echo Source:   %~dp0FortniteStatus.cs
echo Output:   %~dp0FortniteStatus.exe
echo.
echo Compiling...
echo.

"%CSC%" /nologo /target:winexe /platform:anycpu /out:"%~dp0FortniteStatus.exe" ^
 /reference:"%REF%\PresentationFramework.dll" ^
 /reference:"%REF%\PresentationCore.dll" ^
 /reference:"%REF%\WindowsBase.dll" ^
 /reference:"%REF%\System.Xaml.dll" ^
 /reference:"%REF%\System.Net.Http.dll" ^
 "%~dp0FortniteStatus.cs"

echo.
if %errorlevel%==0 (
  echo Build OK: FortniteStatus.exe
  echo Done.
) else (
  echo Build FAILED. See the messages above.
)
echo.
echo Press any key to close this window...
pause >nul
endlocal
