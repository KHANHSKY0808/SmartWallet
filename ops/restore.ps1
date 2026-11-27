param(
    [Parameter(Mandatory = $true)]
    [string]$BackupId,
    [string]$BackupDir = "",
    [string]$TargetDbDir = "",
    [string]$ComposeFile = ""
)

$ErrorActionPreference = "Stop"

$defaultCompose = Join-Path (Split-Path $PSScriptRoot -Parent) "docker-compose.full.yml"
$mode = "docker"
if (-not [string]::IsNullOrWhiteSpace($TargetDbDir)) {
    $mode = "dir"
} elseif (-not [string]::IsNullOrWhiteSpace($BackupDir) -and [string]::IsNullOrWhiteSpace($ComposeFile)) {
    $mode = "dir"
    $TargetDbDir = ".\data"
} else {
    if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
        if (Test-Path ".\docker-compose.full.yml") {
            $ComposeFile = ".\docker-compose.full.yml"
        } elseif (Test-Path $defaultCompose) {
            $ComposeFile = $defaultCompose
        } else {
            $mode = "dir"
            if ([string]::IsNullOrWhiteSpace($BackupDir)) { $BackupDir = ".\backups" }
            if ([string]::IsNullOrWhiteSpace($TargetDbDir)) { $TargetDbDir = ".\data" }
        }
    }
}

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("qlct-restore-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workDir -Force | Out-Null

$volMappings = @(
    @{ Key = "identity";     Vol = "identity_data";     Db = "identity.db" },
    @{ Key = "profile";      Vol = "profile_data";      Db = "profile.db" },
    @{ Key = "wallet";       Vol = "wallet_data";       Db = "wallet.db" },
    @{ Key = "transaction";  Vol = "transaction_data";  Db = "transaction.db" },
    @{ Key = "category";     Vol = "category_data";     Db = "category.db" },
    @{ Key = "budget";       Vol = "budget_data";       Db = "budget.db" },
    @{ Key = "saving";       Vol = "saving_data";       Db = "saving.db" },
    @{ Key = "recdebt";      Vol = "recdebt_data";      Db = "recdebt.db" },
    @{ Key = "report";       Vol = "report_data";       Db = "report.db" },
    @{ Key = "notification"; Vol = "notification_data"; Db = "notification.db" },
    @{ Key = "audit";        Vol = "audit_data";        Db = "audit.db" },
    @{ Key = "file";         Vol = "file_data";         Db = "file.db" }
)

$dotnetServices = @(
    "identityservice", "userprofileservice", "walletservice", "transactionservice",
    "categoryservice", "budgetservice", "savinggoalservice", "recurringdebtservice",
    "reportservice", "notificationservice", "auditservice", "fileservice", "apigateway"
)

function Now-Ts { Get-Date -Format "HH:mm:ss" }

$totalSw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host ("=== BAT DAU KHOI PHUC SMARTWALLET ({0}) luc {1} [Mode: {2}] ===" -f $BackupId, (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $mode)

try {
    if ($mode -eq "docker") {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw "Khong tim thay lenh docker."
        }
        if (-not (Test-Path $ComposeFile)) {
            throw "Khong tim thay Compose file: $ComposeFile"
        }

        # Tra ten Compose project dong (khong hardcode prefix)
        $projName = ""
        $volMatch = (docker volume ls --filter "label=com.docker.compose.volume=qlct_backups" --format "{{.Name}}" 2>$null | Select-Object -First 1)
        if ($volMatch -and $volMatch.EndsWith("_qlct_backups")) {
            $projName = $volMatch.Substring(0, $volMatch.Length - "_qlct_backups".Length)
        }
        if ([string]::IsNullOrWhiteSpace($projName)) {
            $hadJwt = $env:JWT_SIGNING_KEY
            $hadPwd = $env:SEED_ADMIN_PASSWORD
            try {
                if ([string]::IsNullOrWhiteSpace($hadJwt)) { $env:JWT_SIGNING_KEY = "restore-lookup-placeholder-key-0123456789" }
                if ([string]::IsNullOrWhiteSpace($hadPwd)) { $env:SEED_ADMIN_PASSWORD = "placeholder" }
                $cfgJson = docker compose -f $ComposeFile config --format json | Out-String
                $projName = ($cfgJson | ConvertFrom-Json).name
            } finally {
                $env:JWT_SIGNING_KEY = $hadJwt
                $env:SEED_ADMIN_PASSWORD = $hadPwd
            }
        }
        $backupVol = "${projName}_qlct_backups"

        $helperImg = "alpine:latest"
        $null = docker image inspect "redis:alpine" 2>$null
        if ($LASTEXITCODE -eq 0) {
            $helperImg = "redis:alpine"
        } else {
            $null = docker image inspect "alpine:latest" 2>$null
            if ($LASTEXITCODE -ne 0) {
                docker pull -q "alpine:latest" | Out-Null
            }
        }

        # [1/6] Trich xuat zip & manifest.json tu volume backup
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [1/6] Trich xuat ban sao luu {1} tu volume {2}..." -f (Now-Ts), $BackupId, $backupVol)
        $manifestLocal = Join-Path $workDir "manifest.json"
        $zipLocal = Join-Path $workDir "$BackupId.zip"
        $unzipDir = Join-Path $workDir "extracted"
        New-Item -ItemType Directory -Path $unzipDir -Force | Out-Null

        $cid = (docker create --entrypoint sh -v "${backupVol}:/backups:ro" $helperImg -c "true").Trim()
        try {
            docker cp "${cid}:/backups/manifest.json" $manifestLocal 2>$null | Out-Null
            docker cp "${cid}:/backups/$BackupId.zip" $zipLocal 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0 -and (Test-Path $zipLocal)) {
                Expand-Archive -Path $zipLocal -DestinationPath $unzipDir -Force
            } else {
                docker cp "${cid}:/backups/$BackupId/." $unzipDir 2>$null | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Khong tim thay $BackupId.zip hoac thu muc $BackupId trong volume $backupVol"
                }
            }
        } finally {
            docker rm -f $cid 2>$null | Out-Null
        }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [1/6] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        # [2/6] Pre-restore SHA-256 + PRAGMA integrity_check
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [2/6] Doi chieu SHA-256 truoc khi ghi va PRAGMA integrity_check..." -f (Now-Ts))
        $pyPre = Join-Path $workDir "verify_pre.py"
        [System.IO.File]::WriteAllText($pyPre, @'
import hashlib, json, sqlite3, sys
from pathlib import Path
bid, mpath, work = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
if not mpath.exists():
    raise SystemExit(f"Thieu manifest.json tai {mpath}")
entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(mpath.read_text(encoding="utf-8-sig"))]
entry = next((e for e in entries if e.get("backupid") == bid), None)
if not entry:
    raise SystemExit(f"Khong tim thay backupId={bid} trong manifest.json")
files = entry.get("files", [])
if not files:
    raise SystemExit(f"manifest.json khong co files cho {bid}")
for rf in files:
    f = {k.lower(): v for k, v in rf.items()}
    p = work / f["filename"]
    if not p.exists():
        raise SystemExit(f"Thieu file {p}")
    d = hashlib.sha256(p.read_bytes()).hexdigest()
    expected = f["sha256"]
    if d.lower() != expected.lower():
        raise SystemExit(f"Sai sha256 {f['filename']}: {d} != {expected}")
dbs = sorted(work.glob("*.db"))
if len(dbs) < 12:
    raise SystemExit(f"Thieu file .db ({len(dbs)}/12)")
for db in dbs:
    con = sqlite3.connect(str(db))
    res = con.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    if res != "ok":
        raise SystemExit(f"Integrity check failed for {db.name}: {res}")
print(f"Pre-restore verify OK ({len(dbs)} files SHA-256 + integrity_check=ok).")
'@, [System.Text.UTF8Encoding]::new($false))
        python $pyPre $BackupId $manifestLocal $unzipDir
        if ($LASTEXITCODE -ne 0) { throw "Xac minh ban sao luu truoc restore that bai." }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [2/6] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        # [3/6] Stop 13 .NET containers
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [3/6] Dung 13 container .NET (Compose project: {1})..." -f (Now-Ts), $projName)
        docker compose -f $ComposeFile stop apigateway identityservice userprofileservice walletservice transactionservice categoryservice budgetservice savinggoalservice recurringdebtservice reportservice notificationservice auditservice fileservice
        if ($LASTEXITCODE -ne 0) { throw "Dung container .NET that bai." }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [3/6] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        # [4/6] Copy DB vao tung volume rieng, xoa -wal/-shm, chown 10001:10001
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [4/6] Ghi DB vao tung volume rieng, xoa -wal/-shm, dat chown 10001:10001..." -f (Now-Ts))
        $createArgs = @("create", "--entrypoint", "sh")
        $copyCmds = "set -eu;"
        foreach ($m in $volMappings) {
            $createArgs += @("-v", "${projName}_$($m.Vol):/vol/$($m.Key)")
            $copyCmds += " test -f '/restore_src/$($m.Db)'; cp -f '/restore_src/$($m.Db)' '/vol/$($m.Key)/$($m.Db)'; rm -f '/vol/$($m.Key)/$($m.Db)-wal' '/vol/$($m.Key)/$($m.Db)-shm'; chown 10001:10001 '/vol/$($m.Key)' '/vol/$($m.Key)/$($m.Db)';"
            if ($m.Key -eq "transaction") {
                $copyCmds += " mkdir -p '/vol/transaction/imports' && chown 10001:10001 '/vol/transaction/imports';"
            }
        }
        $createArgs += @($helperImg, "-c", $copyCmds)
        $writerCid = (& docker @createArgs).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Tao container ghi volume that bai." }
        try {
            docker cp "$unzipDir\." "${writerCid}:/restore_src/"
            if ($LASTEXITCODE -ne 0) { throw "docker cp vao container ghi volume that bai." }
            docker start -a $writerCid
            if ($LASTEXITCODE -ne 0) { throw "Ghi file .db vao volume Docker that bai." }
        } finally {
            docker rm -f $writerCid 2>$null | Out-Null
        }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [4/6] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        # [5/6] Tu doi chieu SHA-256 trong tung volume sau khi ghi
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [5/6] Tu doi chieu SHA-256 trong tung volume Docker voi manifest.json..." -f (Now-Ts))
        $roArgs = @("run", "--rm", "--entrypoint", "sh")
        $hashCmds = "set -eu;"
        foreach ($m in $volMappings) {
            $roArgs += @("-v", "${projName}_$($m.Vol):/vol/$($m.Key):ro")
            $hashCmds += " sha256sum '/vol/$($m.Key)/$($m.Db)';"
        }
        $roArgs += @($helperImg, "-c", $hashCmds)
        $hashOut = (& docker @roArgs) | Out-String
        if ($LASTEXITCODE -ne 0) { throw "Doc SHA-256 tu volume Docker that bai." }

        $hashTmp = Join-Path $workDir "actual_hashes.txt"
        [System.IO.File]::WriteAllText($hashTmp, $hashOut, [System.Text.UTF8Encoding]::new($false))
        $pyPost = Join-Path $workDir "verify_post.py"
        [System.IO.File]::WriteAllText($pyPost, @'
import json, sys
from pathlib import Path
bid, mpath, hpath = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
actual = {}
for line in hpath.read_text(encoding="utf-8-sig").splitlines():
    line = line.strip()
    if not line: continue
    digest, path = line.split(None, 1)
    actual[Path(path.strip()).name] = digest.lower()
entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(mpath.read_text(encoding="utf-8-sig"))]
entry = next((e for e in entries if e.get("backupid") == bid), None)
for rf in entry.get("files", []):
    f = {k.lower(): v for k, v in rf.items()}
    fn = Path(f["filename"]).name
    if fn not in actual:
        raise SystemExit(f"Thieu {fn} trong volume Docker sau restore")
    expected = f["sha256"].lower()
    if actual[fn] != expected:
        raise SystemExit(f"Post-restore SHA-256 sai cho {fn}: {actual[fn]} != {expected}")
print("Post-restore SHA-256 trong 12/12 volume khop manifest.json 100%.")
'@, [System.Text.UTF8Encoding]::new($false))
        python $pyPost $BackupId $manifestLocal $hashTmp
        if ($LASTEXITCODE -ne 0) { throw "Post-restore SHA-256 verification that bai." }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [5/6] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        # [6/6] Start 13 .NET services & wait /health 8000..8012
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [6/6] Khoi dong lai 13 container .NET va cho /health (8000-8012)..." -f (Now-Ts))
        docker compose -f $ComposeFile start @dotnetServices
        if ($LASTEXITCODE -ne 0) { throw "Khoi dong lai container .NET that bai." }
        foreach ($port in 8000..8012) {
            $ok = $false
            for ($i = 0; $i -lt 90; $i++) {
                try {
                    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 2
                    if ($r.StatusCode -eq 200) { $ok = $true; break }
                } catch {}
                Start-Sleep -Seconds 1
            }
            if (-not $ok) { throw "Cong $port khong healthy sau khoi phuc." }
            Write-Host "  -> :$port/health OK"
        }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [6/6] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)
    } else {
        if ([string]::IsNullOrWhiteSpace($BackupDir)) { $BackupDir = ".\backups" }
        $zipFile = Join-Path $BackupDir "$BackupId.zip"
        $rawDir = Join-Path $BackupDir $BackupId

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [1/5] Kiem tra ban sao luu {1}..." -f (Now-Ts), $BackupId)
        if (Test-Path $zipFile) {
            Expand-Archive -Path $zipFile -DestinationPath $workDir -Force
        } elseif (Test-Path $rawDir) {
            Copy-Item -Path (Join-Path $rawDir "*") -Destination $workDir -Recurse -Force
        } else {
            throw "Khong tim thay $zipFile hoac $rawDir"
        }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [1/5] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [2/5] Doi chieu SHA-256 voi manifest.json va PRAGMA integrity_check..." -f (Now-Ts))
        $manifestPath = Join-Path $BackupDir "manifest.json"
        $pyDir = Join-Path $workDir "verify_dir.py"
        [System.IO.File]::WriteAllText($pyDir, @'
import hashlib, json, sqlite3, sys
from pathlib import Path
bid, mpath, work = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
if mpath.exists():
    entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(mpath.read_text(encoding="utf-8-sig"))]
    entry = next((e for e in entries if e.get("backupid") == bid), None)
    if entry:
        for rf in entry.get("files", []):
            f = {k.lower(): v for k, v in rf.items()}
            p = work / f["filename"]
            if not p.exists():
                raise SystemExit(f"Thieu file {p}")
            d = hashlib.sha256(p.read_bytes()).hexdigest()
            expected = f["sha256"]
            if d.lower() != expected.lower():
                raise SystemExit(f"Sai sha256 {f['filename']}")
for db in sorted(work.glob("*.db")):
    con = sqlite3.connect(str(db))
    res = con.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    if res != "ok":
        raise SystemExit(f"Integrity check failed for {db.name}: {res}")
print("SHA-256 + SQLite integrity_check OK.")
'@, [System.Text.UTF8Encoding]::new($false))
        python $pyDir $BackupId $manifestPath $workDir
        if ($LASTEXITCODE -ne 0) { throw "Xac minh ban sao luu that bai." }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [2/5] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [3/5] Dung cac dich vu neu dung Docker Compose..." -f (Now-Ts))
        if ($ComposeFile -and (Test-Path $ComposeFile)) {
            docker compose -f $ComposeFile stop @dotnetServices
        }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [3/5] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [4/5] Chep cac file .db va xoa -wal / -shm vao {1}..." -f (Now-Ts), $TargetDbDir)
        New-Item -ItemType Directory -Path $TargetDbDir -Force | Out-Null
        Get-ChildItem -Path $workDir -Filter "*.db" | ForEach-Object {
            $dest = Join-Path $TargetDbDir $_.Name
            Copy-Item -Path $_.FullName -Destination $dest -Force
            Remove-Item -Path "$dest-wal", "$dest-shm" -Force -ErrorAction SilentlyContinue
        }
        $sw.Stop()
        Write-Host ("  -> Hoan tat [4/5] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Host ("[{0}] [5/5] Khoi dong lai dich vu & kiem tra /health..." -f (Now-Ts))
        if ($ComposeFile -and (Test-Path $ComposeFile)) {
            docker compose -f $ComposeFile start @dotnetServices
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
        $sw.Stop()
        Write-Host ("  -> Hoan tat [5/5] trong {0:F2}s" -f $sw.Elapsed.TotalSeconds)
    }

    $totalSw.Stop()
    Write-Host ("Hoan tat khoi phuc tu ban {0}. Tong thoi gian (RTO): {1:F2}s" -f $BackupId, $totalSw.Elapsed.TotalSeconds)
} finally {
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
