<#
.SYNOPSIS
  Lets the Job Hunter bot submit Djinni applications on your behalf (the «📨 Подати на Djinni» button).

.DESCRIPTION
  The bot needs your Djinni login: the "sessionid" cookie of a browser where you are logged in.
  1. Log in to djinni.co in your usual Chrome.
  2. F12 → Application → Cookies → https://djinni.co → copy the value of "sessionid".
  3. Run this script and paste it. It checks the session with Djinni, writes it into the server's .env
     over SSH (secret never on a command line or in history) and restarts the bot.

  Do not press "Вийти" (log out) in that browser: logging out kills this session for the bot too.
  When the session expires, the bot says so; run the script again.

.EXAMPLE
  ./scripts/connect-djinni.ps1 -SshHost my-server -ComposeDir /opt/jobhunter -Service jobhunter

.EXAMPLE
  # a shared compose file that maps ${DJINNI_SESSION_ID} into the container
  ./scripts/connect-djinni.ps1 -SshHost my-server -ComposeDir /opt/sites -Service systematia -ComposeVariables
#>
param(
    [Parameter(Mandatory)] [string] $SshHost,
    [string] $ComposeDir = "/opt/jobhunter",
    [string] $Service = "jobhunter",
    # .env holds DJINNI_SESSION_ID instead of Djinni__SessionId
    [switch] $ComposeVariables
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Net.Http
. (Join-Path $PSScriptRoot "ServerEnv.ps1")

Write-Host "Chrome → djinni.co (увійди) → F12 → Application → Cookies → https://djinni.co → значення sessionid`n"
$session = [Net.NetworkCredential]::new("", (Read-Host "sessionid" -AsSecureString)).Password.Trim()
if ($session -notmatch '^[A-Za-z0-9]{16,128}$') { throw "Це не схоже на sessionid: очікую 16–128 латинських літер і цифр." }

# Same check the bot does: a logged-in session gets /my/profile/, an expired one is redirected to /login.
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
try {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "https://djinni.co/my/profile/")
    $request.Headers.Add("Cookie", "sessionid=$session")
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    if ([int]$response.StatusCode -ne 200) { throw "Djinni не прийняв цей sessionid (HTTP $([int]$response.StatusCode)). Скопіюй ще раз з браузера, де ти залогінений." }
}
finally {
    $client.Dispose()
}
Write-Host "Сесія робоча ✅"

$name = if ($ComposeVariables) { "DJINNI_SESSION_ID" } else { "Djinni__SessionId" }
Set-ServerEnv -SshHost $SshHost -ComposeDir $ComposeDir -Service $Service -Values ([ordered]@{ $name = $session })

Write-Host "`n✅ Djinni підключено. У боті: /status → «Автоподача Djinni: ✅ сесія працює»."
