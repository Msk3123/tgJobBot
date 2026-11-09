<#
.SYNOPSIS
  Connects Gmail to a deployed Job Hunter bot in one go.

.DESCRIPTION
  1. Asks for the OAuth client (Google Cloud -> Clients -> Desktop app).
  2. Runs `gmail-auth` locally: a browser opens, you sign in to the mailbox with Djinni/LinkedIn mail.
  3. Writes the client id, client secret and refresh token into the server's .env over SSH
     (a timestamped backup of .env is kept next to it) and restarts the bot.

  Secrets never appear on a command line, in the console, in the repo or in shell history:
  the CLI gets them through environment variables of this process, the server gets them base64-encoded on SSH stdin.

.EXAMPLE
  # docker-compose.yml from this repo (.env holds Gmail__* directly)
  ./scripts/connect-gmail.ps1 -SshHost my-server -ComposeDir /opt/jobhunter -Service jobhunter

.EXAMPLE
  # a shared compose file that maps ${GMAIL_CLIENT_ID} etc. into the container
  ./scripts/connect-gmail.ps1 -SshHost my-server -ComposeDir /opt/sites -Service systematia -ComposeVariables
#>
param(
    [Parameter(Mandatory)] [string] $SshHost,
    [string] $ComposeDir = "/opt/jobhunter",
    [string] $Service = "jobhunter",
    # .env holds GMAIL_CLIENT_ID / GMAIL_CLIENT_SECRET / GMAIL_REFRESH_TOKEN instead of Gmail__ClientId / ...
    [switch] $ComposeVariables
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # the CLI prints Ukrainian text
$repo = Split-Path $PSScriptRoot -Parent
$cli = Join-Path $repo "src/SystematiaBot.Cli"

$clientId = (Read-Host "OAuth Client ID").Trim()
$clientSecret = [Net.NetworkCredential]::new("", (Read-Host "OAuth Client secret" -AsSecureString)).Password.Trim()
if (-not $clientId -or -not $clientSecret) { throw "Client ID і Client secret обов'язкові." }

$env:Gmail__ClientId = $clientId
$env:Gmail__ClientSecret = $clientSecret
$script:token = $null
try {
    Write-Host "`nЗараз відкриється браузер. Увійди в акаунт, куди приходять листи Djinni і LinkedIn.`n"
    & dotnet run --project $cli -- gmail-auth 2>&1 | ForEach-Object {
        $line = "$_"
        if ($line -match '^Gmail__RefreshToken=(.+)$') { $script:token = $Matches[1].Trim() }
        else { Write-Host $line }
    }
}
finally {
    Remove-Item Env:Gmail__ClientId, Env:Gmail__ClientSecret -ErrorAction SilentlyContinue
}

if (-not $script:token) { throw "Refresh token не отримано, дивись повідомлення вище." }

$names = if ($ComposeVariables) { "GMAIL_CLIENT_ID", "GMAIL_CLIENT_SECRET", "GMAIL_REFRESH_TOKEN" }
         else { "Gmail__ClientId", "Gmail__ClientSecret", "Gmail__RefreshToken" }
$values = $clientId, $clientSecret, $script:token

$lines = @(
    "set -e",
    "cd '$ComposeDir'",
    "touch .env && cp -a .env "".env.bak-`$(date +%Y%m%d_%H%M%S)""",
    "set_var() { tmp=`$(mktemp); grep -v ""^`$1="" .env > ""`$tmp"" || true; printf '%s=%s\n' ""`$1"" ""`$(printf %s ""`$2"" | base64 -d)"" >> ""`$tmp""; cat ""`$tmp"" > .env; rm -f ""`$tmp""; }"
)
for ($i = 0; $i -lt 3; $i++) {
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($values[$i]))
    $lines += "set_var $($names[$i]) $encoded"
}
$lines += "docker compose up -d $Service"
$lines += "echo GMAIL-CONNECTED"
$lines += "exit 0"

Write-Host "`nЗаписую в $SshHost`:$ComposeDir/.env і перезапускаю $Service…"
$result = (($lines -join "`n") + "`n") | ssh $SshHost "bash -s" 2>&1
$result | ForEach-Object { Write-Host $_ }
if (-not ($result -match "GMAIL-CONNECTED")) { throw "На сервері щось пішло не так, дивись вивід вище." }

Write-Host "`n✅ Gmail підключено. Перевір у боті: /status"
