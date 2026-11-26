#!/usr/bin/env bash
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Cach dung: $0 <backupId> [backupDir] [dataDir] [composeFile]"
  exit 1
fi

BACKUP_ID="$1"
BACKUP_DIR="${2:-./backups}"
DATA_DIR="${3:-./data}"
COMPOSE_FILE="${4:-docker-compose.full.yml}"
ZIP_FILE="${BACKUP_DIR}/${BACKUP_ID}.zip"
WORK_DIR="$(mktemp -d)"

cleanup() { rm -rf "$WORK_DIR"; }
trap cleanup EXIT

echo "[1/5] Kiem tra ban sao luu ${BACKUP_ID}..."
if [ -f "$ZIP_FILE" ]; then
  unzip -q "$ZIP_FILE" -d "$WORK_DIR"
elif [ -d "${BACKUP_DIR}/${BACKUP_ID}" ]; then
  cp -a "${BACKUP_DIR}/${BACKUP_ID}/." "$WORK_DIR/"
else
  echo "ERROR: Khong tim thay ${ZIP_FILE} hoac thu muc ${BACKUP_DIR}/${BACKUP_ID}"
  exit 1
fi

echo "[2/5] Doi chieu SHA-256 voi manifest.json va PRAGMA integrity_check..."
python3 - "$BACKUP_ID" "$BACKUP_DIR/manifest.json" "$WORK_DIR" <<'PYEOF'
import hashlib, json, sqlite3, sys
from pathlib import Path

bid, manifest_path, work = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
if manifest_path.exists():
    entries = [{k.lower(): v for k, v in e.items()} for e in json.loads(manifest_path.read_text(encoding="utf-8"))]
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

echo "[3/5] Dung cac microservices & Gateway..."
if [ -f "$COMPOSE_FILE" ] && command -v docker >/dev/null 2>&1; then
  docker compose -f "$COMPOSE_FILE" stop apigateway identityservice userprofileservice walletservice transactionservice categoryservice budgetservice savinggoalservice recurringdebtservice reportservice notificationservice auditservice fileservice || true
fi

echo "[4/5] Chep DB phuc hoi va xoa file -wal / -shm..."
mkdir -p "$DATA_DIR"
for db_file in "$WORK_DIR"/*.db; do
  base="$(basename "$db_file")"
  cp -f "$db_file" "${DATA_DIR}/${base}"
  rm -f "${DATA_DIR}/${base}-wal" "${DATA_DIR}/${base}-shm"
done

echo "[5/5] Khoi dong lai cac service va cho /health..."
if [ -f "$COMPOSE_FILE" ] && command -v docker >/dev/null 2>&1; then
  docker compose -f "$COMPOSE_FILE" start identityservice userprofileservice walletservice transactionservice categoryservice budgetservice savinggoalservice recurringdebtservice reportservice notificationservice auditservice fileservice apigateway
  for port in 8000 8001 8002 8003 8004 8005 8006 8007 8008 8009 8010 8011 8012; do
    until curl -fsS "http://127.0.0.1:${port}/health" >/dev/null 2>&1; do
      sleep 1
    done
    echo "  -> :${port}/health OK"
  done
fi

echo "Hoan tat khoi phuc tu ban ${BACKUP_ID}."

