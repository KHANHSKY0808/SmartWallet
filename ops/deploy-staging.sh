#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${1:-docker-compose.full.yml}"
GATEWAY_URL="${GATEWAY_URL:-http://localhost:8000}"
MAILHOG_URL="${MAILHOG_URL:-http://localhost:8025}"

echo "[1/4] Build & start Staging stack ($COMPOSE_FILE)..."
if [ ! -f ".env" ] && [ -f ".env.example" ]; then
  echo "Chua co .env; vui long tao .env tu .env.example truoc khi chay production."
  exit 1
fi

docker compose -f "$COMPOSE_FILE" build
docker compose -f "$COMPOSE_FILE" up -d --wait

echo "[2/4] Smoke test /health cua 13 thanh phan (8000-8012)..."
for port in $(seq 8000 8012); do
  code=$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:${port}/health")
  echo "  -> :${port}/health => HTTP ${code}"
  if [ "$code" != "200" ]; then
    echo "ERROR: Service tren cong ${port} chua healthy!"
    exit 1
  fi
done

echo "[3/4] Smoke test Frontend (:4200)..."
fe_code=$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:4200/")
echo "  -> :4200 => HTTP ${fe_code}"
[ "$fe_code" = "200" ] || { echo "ERROR: Frontend khong phan hoi 200"; exit 1; }

echo "[4/4] Smoke test end-to-end luong dang ky -> xac nhan email -> dang nhap..."
python3 - "$GATEWAY_URL" "$MAILHOG_URL" <<'PYEOF'
import json, re, sys, time, urllib.parse, urllib.request, uuid

gw, mh = sys.argv[1].rstrip("/"), sys.argv[2].rstrip("/")
email = f"smoke.{uuid.uuid4().hex[:8]}@example.com"
pw = "SmokeTest@12345"

def req(method, url, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"} if body is not None else {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    r = urllib.request.Request(url, data=data, headers=headers, method=method)
    with urllib.request.urlopen(r, timeout=10) as resp:
        raw = resp.read().decode("utf-8", errors="replace")
        return resp.status, (json.loads(raw) if raw.strip().startswith("{") else raw)

st, _ = req("POST", f"{gw}/api/auth/register", {
    "email": email, "password": pw, "confirmPassword": pw, "fullName": "Staging Smoke"
})
assert st == 201, f"Register failed: {st}"

tok = None
for _ in range(30):
    try:
        _, res = req("GET", f"{mh}/api/v2/search?kind=to&query={urllib.parse.quote(email)}")
        for item in res.get("items", []):
            m = re.search(r"/confirm-email\?token=([0-9a-fA-F]{64})", item["Content"]["Body"].replace("=\r\n", "").replace("=3D", "="))
            if m:
                tok = m.group(1)
                break
        if tok:
            break
    except Exception:
        pass
    time.sleep(1)
assert tok, "Khong nhan duoc email xac nhan tu MailHog"

st, _ = req("GET", f"{gw}/api/auth/confirm-email?token={tok}")
assert st == 200, f"Confirm failed: {st}"

st, lg = req("POST", f"{gw}/api/auth/login", {"email": email, "password": pw})
assert st == 200 and lg.get("accessToken"), f"Login failed: {st}"

st, prof = req("GET", f"{gw}/api/profile", token=lg["accessToken"])
assert st == 200, f"Profile GET failed: {st}"
print(f"SMOKE TEST PASS: {email} -> register(201) -> confirm(200) -> login(200) -> profile(200)")
PYEOF

