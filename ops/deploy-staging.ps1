param(
    [string]$ComposeFile = "docker-compose.full.yml",
    [string]$GatewayUrl = "http://localhost:8000",
    [string]$MailHogUrl = "http://localhost:8025"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path ".env")) {
    throw "Chua co .env; vui long tao .env tu .env.example."
}

Write-Host "[1/4] Build & start Staging stack ($ComposeFile)..."
docker compose -f $ComposeFile build
if ($LASTEXITCODE -ne 0) { throw "Docker build that bai." }
docker compose -f $ComposeFile up -d --wait
if ($LASTEXITCODE -ne 0) { throw "Docker compose up that bai." }

Write-Host "[2/4] Smoke test /health cua 13 thanh phan (8000-8012)..."
foreach ($port in 8000..8012) {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 5
    Write-Host "  -> :$port/health => HTTP $($resp.StatusCode)"
    if ($resp.StatusCode -ne 200) { throw "Port $port khong healthy" }
}

Write-Host "[3/4] Smoke test Frontend (:4200)..."
$fe = Invoke-WebRequest -Uri "http://127.0.0.1:4200/" -UseBasicParsing -TimeoutSec 5
Write-Host "  -> :4200 => HTTP $($fe.StatusCode)"
if ($fe.StatusCode -ne 200) { throw "Frontend khong phan hoi 200" }

Write-Host "[4/4] Smoke test end-to-end luong dang ky -> xac nhan email -> dang nhap..."
python -c @"
import json, re, sys, time, urllib.parse, urllib.request, uuid
gw, mh = sys.argv[1].rstrip('/'), sys.argv[2].rstrip('/')
email = f'smoke.{uuid.uuid4().hex[:8]}@example.com'
pw = 'SmokeTest@12345'
def req(m, u, b=None, t=None):
    d = json.dumps(b).encode() if b is not None else None
    h = {'Content-Type': 'application/json'} if b is not None else {}
    if t: h['Authorization'] = f'Bearer {t}'
    r = urllib.request.Request(u, data=d, headers=h, method=m)
    with urllib.request.urlopen(r, timeout=10) as resp:
        raw = resp.read().decode('utf-8', errors='replace')
        return resp.status, (json.loads(raw) if raw.strip().startswith('{') else raw)
assert req('POST', f'{gw}/api/auth/register', {'email': email, 'password': pw, 'confirmPassword': pw, 'fullName': 'Staging Smoke'})[0] == 201
tok = None
for _ in range(30):
    try:
        _, res = req('GET', f'{mh}/api/v2/search?kind=to&query={urllib.parse.quote(email)}')
        for it in res.get('items', []):
            m = re.search(r'/confirm-email\?token=([0-9a-fA-F]{64})', it['Content']['Body'].replace('=\r\n', '').replace('=3D', '='))
            if m: tok = m.group(1); break
        if tok: break
    except Exception: pass
    time.sleep(1)
assert tok, 'Khong tim thay token xac nhan trong MailHog'
assert req('GET', f'{gw}/api/auth/confirm-email?token={tok}')[0] == 200
st, lg = req('POST', f'{gw}/api/auth/login', {'email': email, 'password': pw})
assert st == 200 and lg.get('accessToken')
assert req('GET', f'{gw}/api/profile', t=lg['accessToken'])[0] == 200
print(f'SMOKE TEST PASS: {email} -> register(201) -> confirm(200) -> login(200) -> profile(200)')
"@ $GatewayUrl $MailHogUrl
if ($LASTEXITCODE -ne 0) { throw "Smoke test luong auth that bai." }

