@echo off
setlocal
set ROOT=%~dp0
set LOG=%ROOT%setup4-log.txt
cd /d "%ROOT%src\DocAssistant.Api"
(
  echo ===== JWT signing key
  dotnet user-secrets list | findstr /C:"Jwt:Key" >nul
  if errorlevel 1 (
    for /f "usebackq delims=" %%k in (`powershell -NoProfile -Command "[guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')"`) do dotnet user-secrets set "Jwt:Key" "%%k"
    echo New JWT key saved in user-secrets
  ) else (
    echo JWT key already set
  )
  echo.
  echo ===== Restore + build
  dotnet restore
  dotnet build --no-restore
  echo.
  echo ===== Migration for users + document owners
  dir /b Migrations\*_AddUsersAndOwnership.cs >nul 2>&1
  if errorlevel 1 (
    dotnet ef migrations add AddUsersAndOwnership
  ) else (
    echo Migration already exists
  )
  echo.
  echo ===== Database update
  dotnet ef database update
  echo.
  echo ===== Unit tests
  cd /d "%ROOT%"
  dotnet test tests\DocAssistant.Tests\DocAssistant.Tests.csproj
  echo ===== DONE
) > "%LOG%" 2>&1
type "%LOG%"
echo.
echo Saved to setup4-log.txt
pause
