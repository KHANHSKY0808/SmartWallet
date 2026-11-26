param(
    [Parameter(Mandatory = $true)]
    [string]$BackupId,
    [string]$BackupDir = ".\backups",
    [string]$TargetDbDir = ".\data",
    [string]$ComposeFile = ""
)

$ErrorActionPreference = "Stop"
$zipFile = Join-Path $BackupDir "$BackupId.zip"
$rawDir = Join-Path $BackupDir $BackupId
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("qlct-restore-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

try {
    Write-Host "[1/5] Kiem tra ban sao luu $BackupId..."
    if (Test-Path $zipFile) {
        Expand-Archive -Path $zipFile -DestinationPath $workDir -Force
    } elseif (Test-Path $rawDir) {
        Copy-Item -Path (Join-Path $rawDir "*") -Destination $workDir -Recurse -Force
    } else {
        throw "Khong tim thay $zipFile hoac $rawDir"
    }

    Write-Host "[2/5] Doi chieu SHA-256 voi manifest.json va PRAGMA integrity_check..."
    $manifestPath = Join-Path $BackupDir "manifest.json"
    python -c @"
import hashlib, json, sqlite3, sys
from pathlib import Path
bid, mpath, work = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
if mpath.exists():
    entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(mpath.read_text(encoding='utf-8'))]
    entry = next((e for e in entries if e.get('backupid') == bid), None)
    if entry:
        for rf in entry.get('files', []):
            f = {k.lower(): v for k, v in rf.items()}
            p = work / f['filename']
            if not p.exists():
                raise SystemExit(f'Thieu file {p}')
            d = hashlib.sha256(p.read_bytes()).hexdigest()
            if d.lower() != f['sha256'].lower():
                raise SystemExit(f'Sai sha256 {f["filename"]}')
for db in sorted(work.glob('*.db')):
    con = sqlite3.connect(str(db))
    res = con.execute('PRAGMA integrity_check').fetchone()[0]
    con.close()
    if res != 'ok':
        raise SystemExit(f'Integrity check failed for {db.name}: {res}')
print('SHA-256 + SQLite integrity_check OK.')
"@ $BackupId $manifestPath $workDir
    if ($LASTEXITCODE -ne 0) { throw "Xac minh ban sao luu that bai." }

    Write-Host "[3/5] Dung cac dich vu neu dung Docker Compose..."
    if ($ComposeFile -and (Test-Path $ComposeFile)) {
        docker compose -f $ComposeFile stop apigateway identityservice userprofileservice walletservice transactionservice categoryservice budgetservice savinggoalservice recurringdebtservice reportservice notificationservice auditservice fileservice
    }

    Write-Host "[4/5] Chep cac file .db va xoa -wal / -shm..."
    New-Item -ItemType Directory -Path $TargetDbDir -Force | Out-Null
    Get-ChildItem -Path $workDir -Filter "*.db" | ForEach-Object {
        $dest = Join-Path $TargetDbDir $_.Name
        Copy-Item -Path $_.FullName -Destination $dest -Force
        Remove-Item -Path "$dest-wal", "$dest-shm" -Force -ErrorAction SilentlyContinue
    }

    Write-Host "[5/5] Khoi dong lai dich vu & kiem tra /health..."
    if ($ComposeFile -and (Test-Path $ComposeFile)) {
        docker compose -f $ComposeFile start identityservice userprofileservice walletservice transactionservice categoryservice budgetservice savinggoalservice recurringdebtservice reportservice notificationservice auditservice fileservice apigateway
        foreach ($port in 8000..8012) {
            $ok = $false
            for ($i = 0; $i -lt 60; $i++) {
                try {
                    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 2
                    if ($r.StatusCode -eq 200) { $ok = $true; break }
                } catch {}
                Start-Sleep -Seconds 1
            }
            if (-not $ok) { throw "Cong $port khong healthy sau khoi phuc." }
            Write-Host "  -> :$port/health OK"
        }
    }

    Write-Host "Hoan tat khoi phuc tu ban $BackupId."
} finally {
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

