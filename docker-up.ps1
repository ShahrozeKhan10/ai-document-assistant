# Runs the whole app in Docker: SQL Server, Qdrant, API and web app.
# Creates .env on first run (reusing the Gemini key saved by set-gemini-key.bat).
$ErrorActionPreference = "Continue"
$root = $PSScriptRoot
$envFile = Join-Path $root ".env"
$log = Join-Path $root "docker-up-log.txt"

function New-RandomText([int]$length) {
    $chars = [char[]](48..57 + 65..90 + 97..122)
    -join (1..$length | ForEach-Object { $chars | Get-Random })
}

if (-not (Test-Path $envFile)) {
    $secrets = dotnet user-secrets list --project (Join-Path $root "src\DocAssistant.Api")
    $gemini = ($secrets | Where-Object { $_ -like "Gemini:ApiKey = *" }) -replace '^Gemini:ApiKey = ', ''
    if (-not $gemini) { Write-Host "Gemini key not found - run set-gemini-key.bat first." -ForegroundColor Red; exit 1 }

    @(
        "SA_PASSWORD=Doc_$(New-RandomText 12)a1!"
        "GEMINI_API_KEY=$gemini"
        "JWT_KEY=$(New-RandomText 48)"
    ) | Set-Content -Path $envFile -Encoding ascii
    Write-Host "Created .env (it is in .gitignore)"
}

Set-Location $root
"===== docker compose up -d --build" | Out-File $log -Encoding utf8
docker compose up -d --build *>&1 | Tee-Object -FilePath $log -Append
"" | Out-File $log -Append -Encoding utf8
"===== docker compose ps" | Out-File $log -Append -Encoding utf8
docker compose ps *>&1 | Tee-Object -FilePath $log -Append

Write-Host ""
Write-Host "Starting... SQL Server needs about 30 seconds the first time."
Write-Host "Then open http://localhost:3000" -ForegroundColor Green
Write-Host "Stop everything with: docker compose down"
