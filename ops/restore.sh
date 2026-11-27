#!/usr/bin/env bash
set -euo pipefail

# Tranh MSYS2/Git Bash tu dong chuyen path container (/vol/..., /backups/...) sang Windows path
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL="*"

if [ $# -lt 1 ]; then
  echo "Cach dung:"
  echo "  Che do Docker volume (mac dinh): $0 <backupId> [composeFile]"
  echo "  Che do thu muc local:           $0 <backupId> <backupDir> <dataDir> [composeFile]"
  exit 1
fi

BACKUP_ID="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_COMPOSE="${SCRIPT_DIR}/../docker-compose.full.yml"

MODE="docker"
BACKUP_DIR="./backups"
DATA_DIR=""
COMPOSE_FILE=""

if [ $# -ge 3 ] && [ -n "${3:-}" ]; then
  MODE="dir"
  BACKUP_DIR="${2:-./backups}"
  DATA_DIR="$3"
  COMPOSE_FILE="${4:-}"
elif [ $# -eq 2 ]; then
  if [ -f "$2" ] && [[ "$2" == *.yml || "$2" == *.yaml ]]; then
    MODE="docker"
    COMPOSE_FILE="$2"
  elif [ -d "$2" ]; then
    MODE="dir"
    BACKUP_DIR="$2"
    DATA_DIR="./data"
  else
    COMPOSE_FILE="$2"
  fi
fi

if [ "$MODE" = "docker" ] && [ -z "$COMPOSE_FILE" ]; then
  if [ -f "./docker-compose.full.yml" ]; then
    COMPOSE_FILE="./docker-compose.full.yml"
  elif [ -f "$DEFAULT_COMPOSE" ]; then
    COMPOSE_FILE="$DEFAULT_COMPOSE"
  else
    MODE="dir"
    BACKUP_DIR="./backups"
    DATA_DIR="./data"
  fi
fi

# Chon Python 3 hoat dong duoc tren ca Linux, WSL va Git Bash Windows
PYTHON_BIN=""
if command -v python3 >/dev/null 2>&1 && python3 -c "import sys" >/dev/null 2>&1; then
  PYTHON_BIN="python3"
elif command -v python >/dev/null 2>&1 && python -c "import sys" >/dev/null 2>&1; then
  PYTHON_BIN="python"
else
  echo "ERROR: Khong tim thay python3 hoac python." >&2
  exit 1
fi

to_host_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s\n' "$1"
  fi
}

WORK_DIR="$(mktemp -d)"
cleanup() { rm -rf "$WORK_DIR"; }
trap cleanup EXIT

now_ts() { date +"%H:%M:%S"; }
now_ms() { "$PYTHON_BIN" -c "import time; print(int(time.time() * 1000))"; }
fmt_dur() { "$PYTHON_BIN" -c "import sys; print(f'{(int(sys.argv[2])-int(sys.argv[1]))/1000:.2f}s')" "$1" "$2"; }

DOTNET_SERVICES=(
  identityservice userprofileservice walletservice transactionservice
  categoryservice budgetservice savinggoalservice recurringdebtservice
  reportservice notificationservice auditservice fileservice apigateway
)

VOL_MAPPINGS=(
  "identity:identity_data:identity.db"
  "profile:profile_data:profile.db"
  "wallet:wallet_data:wallet.db"
  "transaction:transaction_data:transaction.db"
  "category:category_data:category.db"
  "budget:budget_data:budget.db"
  "saving:saving_data:saving.db"
  "recdebt:recdebt_data:recdebt.db"
  "report:report_data:report.db"
  "notification:notification_data:notification.db"
  "audit:audit_data:audit.db"
  "file:file_data:file.db"
)

TOTAL_START="$(now_ms)"
echo "=== BAT DAU KHOI PHUC SMARTWALLET (${BACKUP_ID}) luc $(date '+%Y-%m-%d %H:%M:%S %z') [Mode: ${MODE}] ==="

if [ "$MODE" = "docker" ]; then
  if ! command -v docker >/dev/null 2>&1; then
    echo "ERROR: Khong tim thay lenh docker." >&2
    exit 1
  fi

  COMPOSE_HOST="$(to_host_path "$COMPOSE_FILE")"

  # Tra ten Compose project dong (khong hardcode prefix)
  PROJ_NAME="$(JWT_SIGNING_KEY="${JWT_SIGNING_KEY:-restore-lookup-placeholder-key-0123456789}" \
    SEED_ADMIN_PASSWORD="${SEED_ADMIN_PASSWORD:-placeholder}" \
    docker compose -f "$COMPOSE_HOST" config --format json 2>/dev/null \
    | "$PYTHON_BIN" -c "import json,sys; d=json.load(sys.stdin); print(d.get('name',''))" || true)"
  if [ -z "$PROJ_NAME" ]; then
    VOL_MATCH="$(docker volume ls --filter "label=com.docker.compose.volume=qlct_backups" --format "{{.Name}}" || true)"
    VOL_MATCH="${VOL_MATCH%%$'\n'*}"
    if [ -n "$VOL_MATCH" ] && [[ "$VOL_MATCH" == *_qlct_backups ]]; then
      PROJ_NAME="${VOL_MATCH%_qlct_backups}"
    fi
  fi
  if [ -z "$PROJ_NAME" ]; then
    echo "ERROR: Khong xac dinh duoc ten Compose project." >&2
    exit 1
  fi
  BACKUP_VOL="${PROJ_NAME}_qlct_backups"

  HELPER_IMG="alpine:latest"
  if docker image inspect redis:alpine >/dev/null 2>&1; then
    HELPER_IMG="redis:alpine"
  elif ! docker image inspect alpine:latest >/dev/null 2>&1; then
    docker pull -q alpine:latest >/dev/null
  fi

  S1="$(now_ms)"
  echo "[$(now_ts)] [1/6] Trich xuat ban sao luu ${BACKUP_ID} tu volume ${BACKUP_VOL}..."
  TMP_CID="$(docker create --entrypoint sh -v "${BACKUP_VOL}:/backups:ro" "$HELPER_IMG" -c "true")"
  MANIFEST_LOCAL="${WORK_DIR}/manifest.json"
  ZIP_LOCAL="${WORK_DIR}/${BACKUP_ID}.zip"
  UNZIP_DIR="${WORK_DIR}/extracted"
  mkdir -p "$UNZIP_DIR"

  MANIFEST_HOST="$(to_host_path "$MANIFEST_LOCAL")"
  ZIP_HOST="$(to_host_path "$ZIP_LOCAL")"
  UNZIP_HOST="$(to_host_path "$UNZIP_DIR")"

  docker cp "${TMP_CID}:/backups/manifest.json" "$MANIFEST_HOST" >/dev/null 2>&1 || true
  if docker cp "${TMP_CID}:/backups/${BACKUP_ID}.zip" "$ZIP_HOST" >/dev/null 2>&1; then
    "$PYTHON_BIN" -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$ZIP_HOST" "$UNZIP_HOST"
  elif docker cp "${TMP_CID}:/backups/${BACKUP_ID}/." "$UNZIP_HOST" >/dev/null 2>&1; then
    true
  else
    docker rm -f "$TMP_CID" >/dev/null 2>&1 || true
    echo "ERROR: Khong tim thay ${BACKUP_ID}.zip hoac thu muc ${BACKUP_ID} trong volume ${BACKUP_VOL}" >&2
    exit 1
  fi
  docker rm -f "$TMP_CID" >/dev/null 2>&1 || true
  E1="$(now_ms)"
  echo "  -> Hoan tat [1/6] trong $(fmt_dur "$S1" "$E1")"

  S2="$(now_ms)"
  echo "[$(now_ts)] [2/6] Doi chieu SHA-256 truoc khi ghi va PRAGMA integrity_check..."
  "$PYTHON_BIN" - "$BACKUP_ID" "$MANIFEST_HOST" "$UNZIP_HOST" <<'PYEOF'
import hashlib, json, sqlite3, sys
from pathlib import Path

bid, manifest_path, work = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
if not manifest_path.exists():
    raise SystemExit(f"Thieu manifest.json tai {manifest_path}")
entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(manifest_path.read_text(encoding="utf-8-sig"))]
entry = next((e for e in entries if e.get("backupid") == bid), None)
if not entry:
    raise SystemExit(f"Khong tim thay backupId={bid} trong manifest.json")
files = entry.get("files", [])
if not files:
    raise SystemExit(f"manifest.json khong co danh sach files cho {bid}")
for raw_f in files:
    f = {k.lower(): v for k, v in raw_f.items()}
    p = work / f["filename"]
    if not p.exists():
        raise SystemExit(f"Thieu file {p}")
    digest = hashlib.sha256(p.read_bytes()).hexdigest()
    if digest.lower() != f["sha256"].lower():
        raise SystemExit(f"Sai sha256 cho {f['filename']}: {digest} != {f['sha256']}")

db_files = sorted(work.glob("*.db"))
if len(db_files) < 12:
    raise SystemExit(f"Thieu file .db (chi co {len(db_files)}/12)")
for db_file in db_files:
    con = sqlite3.connect(str(db_file))
    res = con.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    if res != "ok":
        raise SystemExit(f"SQLite integrity_check that bai cho {db_file.name}: {res}")
print(f"Pre-restore verify OK ({len(db_files)} files SHA-256 + integrity_check=ok).")
PYEOF
  E2="$(now_ms)"
  echo "  -> Hoan tat [2/6] trong $(fmt_dur "$S2" "$E2")"

  S3="$(now_ms)"
  echo "[$(now_ts)] [3/6] Dung 13 container .NET (Compose project: ${PROJ_NAME})..."
  docker compose -f "$COMPOSE_HOST" stop "${DOTNET_SERVICES[@]}"
  E3="$(now_ms)"
  echo "  -> Hoan tat [3/6] trong $(fmt_dur "$S3" "$E3")"

  S4="$(now_ms)"
  echo "[$(now_ts)] [4/6] Ghi DB vao tung volume rieng, xoa -wal/-shm, dat chown 10001:10001..."
  VOL_ARGS=()
  COPY_CMDS="set -eu;"
  for m in "${VOL_MAPPINGS[@]}"; do
    IFS=":" read -r key vol db <<< "$m"
    VOL_ARGS+=("-v" "${PROJ_NAME}_${vol}:/vol/${key}")
    COPY_CMDS="${COPY_CMDS} test -f \"/restore_src/${db}\"; cp -f \"/restore_src/${db}\" \"/vol/${key}/${db}\"; rm -f \"/vol/${key}/${db}-wal\" \"/vol/${key}/${db}-shm\"; chown 10001:10001 \"/vol/${key}\" \"/vol/${key}/${db}\";"
    if [ "$key" = "transaction" ]; then
      COPY_CMDS="${COPY_CMDS} mkdir -p \"/vol/transaction/imports\" && chown 10001:10001 \"/vol/transaction/imports\";"
    fi
  done
  WRITER_CID="$(docker create --entrypoint sh "${VOL_ARGS[@]}" "$HELPER_IMG" -c "$COPY_CMDS")"
  docker cp "${UNZIP_HOST}/." "${WRITER_CID}:/restore_src/"
  docker start -a "$WRITER_CID"
  docker rm -f "$WRITER_CID" >/dev/null 2>&1 || true
  E4="$(now_ms)"
  echo "  -> Hoan tat [4/6] trong $(fmt_dur "$S4" "$E4")"

  S5="$(now_ms)"
  echo "[$(now_ts)] [5/6] Tu doi chieu SHA-256 trong tung volume Docker voi manifest.json..."
  RO_ARGS=()
  HASH_CMDS="set -eu;"
  for m in "${VOL_MAPPINGS[@]}"; do
    IFS=":" read -r key vol db <<< "$m"
    RO_ARGS+=("-v" "${PROJ_NAME}_${vol}:/vol/${key}:ro")
    HASH_CMDS="${HASH_CMDS} sha256sum \"/vol/${key}/${db}\";"
  done
  HASH_FILE="${WORK_DIR}/actual_hashes.txt"
  HASH_HOST="$(to_host_path "$HASH_FILE")"
  docker run --rm --entrypoint sh "${RO_ARGS[@]}" "$HELPER_IMG" -c "$HASH_CMDS" > "$HASH_FILE"
  "$PYTHON_BIN" - "$BACKUP_ID" "$MANIFEST_HOST" "$HASH_HOST" <<'PYEOF'
import json, sys
from pathlib import Path

bid, mpath, hpath = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
actual = {}
for line in hpath.read_text(encoding="utf-8-sig").splitlines():
    line = line.strip()
    if not line:
        continue
    digest, path = line.split(None, 1)
    actual[Path(path.strip()).name] = digest.lower()

entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(mpath.read_text(encoding="utf-8-sig"))]
entry = next((e for e in entries if e.get("backupid") == bid), None)
if not entry:
    raise SystemExit(f"Khong tim thay backupId={bid} trong manifest.json")
for rf in entry.get("files", []):
    f = {k.lower(): v for k, v in rf.items()}
    fn = Path(f["filename"]).name
    if fn not in actual:
        raise SystemExit(f"Thieu {fn} trong volume Docker sau restore")
    if actual[fn] != f["sha256"].lower():
        raise SystemExit(f"Post-restore SHA-256 sai cho {fn}: {actual[fn]} != {f['sha256']}")
print("Post-restore SHA-256 trong 12/12 volume khop manifest.json 100%.")
PYEOF
  E5="$(now_ms)"
  echo "  -> Hoan tat [5/6] trong $(fmt_dur "$S5" "$E5")"

  S6="$(now_ms)"
  echo "[$(now_ts)] [6/6] Khoi dong lai 13 container .NET va cho /health (8000-8012)..."
  docker compose -f "$COMPOSE_HOST" start "${DOTNET_SERVICES[@]}"
  for port in 8000 8001 8002 8003 8004 8005 8006 8007 8008 8009 8010 8011 8012; do
    WAITED=0
    until curl -fsS "http://127.0.0.1:${port}/health" >/dev/null 2>&1; do
      WAITED=$((WAITED + 1))
      if [ "$WAITED" -ge 90 ]; then
        echo "ERROR: Cong ${port} khong healthy sau 90s" >&2
        exit 1
      fi
      sleep 1
    done
    echo "  -> :${port}/health OK"
  done
  E6="$(now_ms)"
  echo "  -> Hoan tat [6/6] trong $(fmt_dur "$S6" "$E6")"
else
  # Che do thu muc local (cho stack dev/QA khong dung Docker volume)
  ZIP_FILE="${BACKUP_DIR}/${BACKUP_ID}.zip"

  S1="$(now_ms)"
  echo "[$(now_ts)] [1/5] Kiem tra ban sao luu ${BACKUP_ID}..."
  WORK_HOST="$(to_host_path "$WORK_DIR")"
  if [ -f "$ZIP_FILE" ]; then
    ZIP_HOST="$(to_host_path "$ZIP_FILE")"
    "$PYTHON_BIN" -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$ZIP_HOST" "$WORK_HOST"
  elif [ -d "${BACKUP_DIR}/${BACKUP_ID}" ]; then
    cp -a "${BACKUP_DIR}/${BACKUP_ID}/." "$WORK_DIR/"
  else
    echo "ERROR: Khong tim thay ${ZIP_FILE} hoac thu muc ${BACKUP_DIR}/${BACKUP_ID}" >&2
    exit 1
  fi
  E1="$(now_ms)"
  echo "  -> Hoan tat [1/5] trong $(fmt_dur "$S1" "$E1")"

  S2="$(now_ms)"
  echo "[$(now_ts)] [2/5] Doi chieu SHA-256 voi manifest.json va PRAGMA integrity_check..."
  MANIFEST_HOST="$(to_host_path "${BACKUP_DIR}/manifest.json")"
  "$PYTHON_BIN" - "$BACKUP_ID" "$MANIFEST_HOST" "$WORK_HOST" <<'PYEOF'
import hashlib, json, sqlite3, sys
from pathlib import Path

bid, manifest_path, work = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
if manifest_path.exists():
    entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(manifest_path.read_text(encoding="utf-8-sig"))]
    entry = next((e for e in entries if e.get("backupid") == bid), None)
    if entry:
        for raw_f in entry.get("files", []):
            f = {k.lower(): v for k, v in raw_f.items()}
            p = work / f["filename"]
            if not p.exists():
                raise SystemExit(f"Thieu file {p}")
            digest = hashlib.sha256(p.read_bytes()).hexdigest()
            if digest.lower() != f["sha256"].lower():
                raise SystemExit(f"Sai sha256 cho {f['filename']}: {digest} != {f['sha256']}")

for db_file in sorted(work.glob("*.db")):
    con = sqlite3.connect(str(db_file))
    res = con.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    if res != "ok":
        raise SystemExit(f"SQLite integrity_check that bai cho {db_file.name}: {res}")
print("SHA-256 va PRAGMA integrity_check deu OK.")
PYEOF
  E2="$(now_ms)"
  echo "  -> Hoan tat [2/5] trong $(fmt_dur "$S2" "$E2")"

  S3="$(now_ms)"
  echo "[$(now_ts)] [3/5] Dung cac service neu co ComposeFile..."
  if [ -n "$COMPOSE_FILE" ] && [ -f "$COMPOSE_FILE" ] && command -v docker >/dev/null 2>&1; then
    docker compose -f "$(to_host_path "$COMPOSE_FILE")" stop "${DOTNET_SERVICES[@]}" || true
  fi
  E3="$(now_ms)"
  echo "  -> Hoan tat [3/5] trong $(fmt_dur "$S3" "$E3")"

  S4="$(now_ms)"
  echo "[$(now_ts)] [4/5] Chep DB phuc hoi va xoa file -wal / -shm vao ${DATA_DIR}..."
  mkdir -p "$DATA_DIR"
  for db_file in "$WORK_DIR"/*.db; do
    base="$(basename "$db_file")"
    cp -f "$db_file" "${DATA_DIR}/${base}"
    rm -f "${DATA_DIR}/${base}-wal" "${DATA_DIR}/${base}-shm"
  done
  E4="$(now_ms)"
  echo "  -> Hoan tat [4/5] trong $(fmt_dur "$S4" "$E4")"

  S5="$(now_ms)"
  echo "[$(now_ts)] [5/5] Khoi dong lai cac service neu co ComposeFile..."
  if [ -n "$COMPOSE_FILE" ] && [ -f "$COMPOSE_FILE" ] && command -v docker >/dev/null 2>&1; then
    docker compose -f "$(to_host_path "$COMPOSE_FILE")" start "${DOTNET_SERVICES[@]}"
    for port in 8000 8001 8002 8003 8004 8005 8006 8007 8008 8009 8010 8011 8012; do
      until curl -fsS "http://127.0.0.1:${port}/health" >/dev/null 2>&1; do
        sleep 1
      done
      echo "  -> :${port}/health OK"
    done
  fi
  E5="$(now_ms)"
  echo "  -> Hoan tat [5/5] trong $(fmt_dur "$S5" "$E5")"
fi

TOTAL_END="$(now_ms)"
echo "Hoan tat khoi phuc tu ban ${BACKUP_ID}. Tong thoi gian (RTO): $(fmt_dur "$TOTAL_START" "$TOTAL_END")"
