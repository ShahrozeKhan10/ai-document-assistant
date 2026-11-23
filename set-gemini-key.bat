@echo off
cd /d "%~dp0src\DocAssistant.Api"
echo Your key is saved with dotnet user-secrets (outside this folder), so it never goes to GitHub.
echo.
set /p KEY=Paste your Gemini API key and press Enter: 
dotnet user-secrets set "Gemini:ApiKey" "%KEY%"
echo.
dotnet user-secrets list | findstr /C:"Gemini:ApiKey" >nul && echo Key saved. || echo Key NOT saved - send me the message above.
pause
