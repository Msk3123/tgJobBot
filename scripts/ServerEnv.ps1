# Shared by connect-*.ps1: writes NAME=value pairs into a server's .env over SSH and restarts one compose service.
# Values travel base64-encoded on SSH stdin, so they never appear on a command line or in shell history.
# The previous .env is kept next to it as .env.bak-<timestamp>.

function Set-ServerEnv {
    param(
        [Parameter(Mandatory)] [string] $SshHost,
        [Parameter(Mandatory)] [string] $ComposeDir,
        [Parameter(Mandatory)] [string] $Service,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Values
    )

    $lines = @(
        "set -e",
        "cd '$ComposeDir'",
        "touch .env && cp -a .env "".env.bak-`$(date +%Y%m%d_%H%M%S)""",
        "set_var() { tmp=`$(mktemp); grep -v ""^`$1="" .env > ""`$tmp"" || true; printf '%s=%s\n' ""`$1"" ""`$(printf %s ""`$2"" | base64 -d)"" >> ""`$tmp""; cat ""`$tmp"" > .env; rm -f ""`$tmp""; }"
    )
    foreach ($name in $Values.Keys) {
        $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$Values[$name]))
        $lines += "set_var $name $encoded"
    }
    $lines += "docker compose up -d $Service"
    $lines += "echo SERVER-ENV-UPDATED"
    $lines += "exit 0"

    Write-Host "`nЗаписую в ${SshHost}:$ComposeDir/.env і перезапускаю $Service…"
    $result = (($lines -join "`n") + "`n") | ssh $SshHost "bash -s" 2>&1
    $result | ForEach-Object { Write-Host $_ }
    if (-not ($result -match "SERVER-ENV-UPDATED")) { throw "На сервері щось пішло не так, дивись вивід вище." }
}
