# Shared by connect-*.ps1: writes NAME=value pairs into a server's .env over SSH and restarts one compose service.
# Values travel base64-encoded on SSH stdin, so they never appear on a command line or in shell history.
# The previous .env is kept next to it as .env.bak-<timestamp>.

function Set-ServerEnv {
    param(
        [Parameter(Mandatory)] [string] $SshHost,
        [Parameter(Mandatory)] [string] $ComposeDir,
        [Parameter(Mandatory)] [string] $Service,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Values,
        # Only writes .env (used to test the script against a scratch directory).
        [switch] $NoRestart
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
    if (-not $NoRestart) { $lines += "docker compose up -d $Service" }
    $lines += "echo SERVER-ENV-UPDATED"
    $lines += "exit 0"

    Write-Host "`nЗаписую в ${SshHost}:$ComposeDir/.env і перезапускаю $Service…"

    # Windows PowerShell pipes text to native programs with a UTF-8 BOM when the console is UTF-8, and bash then
    # reads "<BOM>set" as a command name. So: no-BOM encoding here, and the server strips a BOM and CRs anyway.
    # docker compose reports progress on stderr, which must not count as a failure.
    $savedEncoding = $OutputEncoding
    $savedPreference = $ErrorActionPreference
    try {
        $OutputEncoding = [Text.UTF8Encoding]::new($false)
        $ErrorActionPreference = "Continue"
        $result = (($lines -join "`n") + "`n") | ssh $SshHost "sed '1s/^\xEF\xBB\xBF//' | tr -d '\r' | bash -s" 2>&1 | ForEach-Object { "$_" }
    }
    finally {
        $OutputEncoding = $savedEncoding
        $ErrorActionPreference = $savedPreference
    }

    $result | ForEach-Object { Write-Host $_ }
    if (-not ($result -match "SERVER-ENV-UPDATED")) { throw "На сервері щось пішло не так, дивись вивід вище." }
}
