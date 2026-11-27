import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const outDir = path.join(repoRoot, 'fe', 'src', 'app', 'core', 'api', 'openapi');

const SERVICES = [
  { key: 'identity', project: 'IdentityService', port: 19101 },
  { key: 'user-profile', project: 'UserProfileService', port: 19102 },
  { key: 'wallet', project: 'WalletService', port: 19103 },
  { key: 'category', project: 'CategoryService', port: 19104 },
  { key: 'transaction', project: 'TransactionService', port: 19105 },
  { key: 'budget', project: 'BudgetService', port: 19106 },
  { key: 'saving-goal', project: 'SavingGoalService', port: 19107 },
  { key: 'recurring-debt', project: 'RecurringDebtService', port: 19108 },
  { key: 'report', project: 'ReportService', port: 19109 },
  { key: 'notification', project: 'NotificationService', port: 19110 },
  { key: 'audit', project: 'AuditService', port: 19111 },
  { key: 'file', project: 'FileService', port: 19112 },
];

const configArgIdx = process.argv.indexOf('--configuration');
const requestedConfig = configArgIdx !== -1 ? process.argv[configArgIdx + 1] : null;

function resolveDll(project) {
  const candidates = requestedConfig
    ? [requestedConfig]
    : ['Release', 'Debug'];
  for (const cfg of candidates) {
    const dll = path.join(repoRoot, 'src', 'Services', project, 'bin', cfg, 'net10.0', `${project}.dll`);
    if (fs.existsSync(dll)) return dll;
  }
  throw new Error(`Built DLL not found for ${project} (tried ${candidates.join(', ')}). Run dotnet build first.`);
}

async function waitForOpenApi(url, proc, timeoutMs = 45000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (proc.exitCode !== null) {
      throw new Error(`Process exited early with code ${proc.exitCode} before serving ${url}`);
    }
    try {
      const res = await fetch(url);
      if (res.ok) {
        return await res.json();
      }
    } catch {
      // Service still starting
    }
    await new Promise(r => setTimeout(r, 300));
  }
  throw new Error(`Timed out waiting for ${url}`);
}

async function exportOne(svc, tmpDir) {
  const dll = resolveDll(svc.project);
  const svcDir = path.dirname(dll);
  const dbPath = path.join(tmpDir, `${svc.key}.db`);
  const uploadDir = path.join(tmpDir, `${svc.key}-uploads`);
  fs.mkdirSync(uploadDir, { recursive: true });

  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://127.0.0.1:${svc.port}`,
    ConnectionStrings__DefaultConnection: `Data Source=${dbPath}`,
    Jwt__SigningKey: 'dev-contract-export-signing-key-32-chars-minimum',
    JWT_SIGNING_KEY: 'dev-contract-export-signing-key-32-chars-minimum',
    Consul__Enabled: 'false',
    RabbitMQ__Host: '127.0.0.1',
    RabbitMQ__Port: '59999',
    Storage__UploadPath: uploadDir,
    Import__TempDirectory: uploadDir,
    Export__TempDirectory: uploadDir,
  };

  const proc = spawn('dotnet', [dll, `--urls=http://127.0.0.1:${svc.port}`], {
    cwd: svcDir,
    env,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  let logs = '';
  proc.stdout.on('data', d => { logs += d.toString(); });
  proc.stderr.on('data', d => { logs += d.toString(); });

  try {
    const doc = await waitForOpenApi(`http://127.0.0.1:${svc.port}/openapi/v1.json`, proc);
    const outPath = path.join(outDir, `${svc.key}.json`);
    const json = JSON.stringify(doc, null, 2).replace(/\r\n/g, '\n') + '\n';
    fs.writeFileSync(outPath, json, 'utf8');
    console.log(`Exported ${svc.key}.json (${Object.keys(doc.paths ?? {}).length} paths)`);
  } catch (err) {
    throw new Error(`[${svc.project}] ${err.message}\n${logs.slice(-2000)}`);
  } finally {
    if (proc.exitCode === null) {
      proc.kill('SIGTERM');
    }
  }
}

async function main() {
  fs.mkdirSync(outDir, { recursive: true });
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'qlct-openapi-'));

  try {
    const concurrency = 4;
    for (let i = 0; i < SERVICES.length; i += concurrency) {
      const batch = SERVICES.slice(i, i + concurrency);
      await Promise.all(batch.map(svc => exportOne(svc, tmpDir)));
    }
  } finally {
    await new Promise(r => setTimeout(r, 300));
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});

