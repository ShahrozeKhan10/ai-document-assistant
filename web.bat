@echo off
set ROOT=%~dp0
set LOG=%ROOT%web-log.txt
cd /d "%ROOT%web"
(
  echo ===== node / npm
  node --version
  call npm --version
  echo.
  echo ===== npm install
  if not exist node_modules (
    call npm install --no-audit --no-fund
  ) else (
    echo node_modules already present
  )
  echo.
  echo ===== type-check + build
  call npm run build
  echo ===== DONE
) > "%LOG%" 2>&1
type "%LOG%"
echo.
echo Starting the web app at http://localhost:5173  (keep run.bat open - it runs the API)
echo Press Ctrl+C to stop.
echo.
call npm run dev
pause
