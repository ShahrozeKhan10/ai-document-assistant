@echo off
set ROOT=%~dp0
set LOG=%ROOT%run-log.txt
set QDRANT_EXE=%ROOT%tools\qdrant\qdrant.exe
cd /d "%ROOT%"
(
  echo ===== Start Qdrant
  if exist "%QDRANT_EXE%" (
    echo Using local qdrant.exe
    tasklist /FI "IMAGENAME eq qdrant.exe" | find /I "qdrant.exe" >nul && (echo Qdrant already running) || (
      start "Qdrant" /min /D "%ROOT%tools\qdrant" "%QDRANT_EXE%"
      ping -n 6 127.0.0.1 >nul
    )
  ) else (
    echo qdrant.exe not found in tools\qdrant - trying Docker
    docker compose up -d
  )
  echo.
  echo ===== EF database update
  cd /d "%ROOT%src\DocAssistant.Api"
  dotnet ef database update
  echo.
  echo ===== Build
  dotnet build
  echo ===== DONE
) > "%LOG%" 2>&1
type "%LOG%"
echo.
cd /d "%ROOT%src\DocAssistant.Api"
echo Starting the API. Swagger: http://localhost:5080/swagger   Qdrant dashboard: http://localhost:6333/dashboard
echo This window stays empty - the API log is saved to api-log.txt. Press Ctrl+C to stop.
echo.
dotnet run --launch-profile http > "%ROOT%api-log.txt" 2>&1
pause
