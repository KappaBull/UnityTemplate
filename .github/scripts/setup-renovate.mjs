import { spawn } from 'node:child_process';
import { randomBytes, sign, timingSafeEqual } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parseArgs } from 'node:util';

const root = fileURLToPath(new URL('../../', import.meta.url));
const clientIdName = 'RENOVATE_APP_CLIENT_ID';
const privateKeyName = 'RENOVATE_APP_PRIVATE_KEY';

export class SetupError extends Error {}

export function gh(args, input) {
  return new Promise((resolveResult, reject) => {
    const env = { ...process.env };
    delete env.GH_DEBUG;
    const child = spawn('gh', args, {
      cwd: root, env, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
    });
    let output = '';
    child.stdout.setEncoding('utf8').on('data', chunk => { output += chunk; });
    // API responses and child diagnostics can contain credentials.
    child.stderr.resume();
    child.stdin.on('error', () => {});
    child.on('error', () => reject(new SetupError('GitHub CLI is unavailable. Install gh and sign in before running setup.')));
    child.on('close', code => {
      if (code === 0) resolveResult(output);
      else reject(new SetupError(`GitHub CLI ${args[0]} failed. Check your access to the target repository.`));
    });
    child.stdin.end(input);
  });
}

function escapeHtml(value) {
  return value.replace(/[&<>"']/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[character]);
}

export function buildManifest(base, repository, redirectUrl, name) {
  const manifest = structuredClone(base);
  manifest.name = name || `${repository.owner.login}-${repository.name}-renovate`;
  manifest.url = manifest.url.replace('{{repository}}', repository.full_name);
  manifest.redirect_url = redirectUrl;
  if (repository.owner.type === 'Organization') manifest.default_permissions.members = 'read';
  return manifest;
}

export async function registrationPage(base, repository, name, timeoutMs = 10 * 60 * 1000) {
  const state = randomBytes(32).toString('hex');
  let complete;
  let fail;
  let origin;
  let consumed = false;
  let timer;
  const code = new Promise((resolveCode, rejectCode) => { complete = resolveCode; fail = rejectCode; });
  // Browser launch can fail before the caller starts awaiting the callback.
  code.catch(() => {});
  const server = createServer((request, response) => {
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('Referrer-Policy', 'no-referrer');
    response.setHeader('Content-Type', 'text/html; charset=utf-8');
    response.setHeader('Content-Security-Policy', "default-src 'none'; form-action https://github.com; frame-ancestors 'none'");
    const url = new URL(request.url, origin);
    const receivedState = url.searchParams.get('state') || '';
    const receivedBytes = Buffer.from(receivedState);
    const stateBytes = Buffer.from(state);
    const stateMatches = receivedBytes.length === stateBytes.length
      && timingSafeEqual(receivedBytes, stateBytes);
    if (request.method !== 'GET' || request.headers.host !== new URL(origin).host || !stateMatches) {
      response.writeHead(403).end('This request is not authorized.');
      return;
    }
    if (url.pathname === '/') {
      const manifest = buildManifest(base, repository, `${origin}/callback`, name);
      const ownerPath = repository.owner.type === 'Organization'
        ? `/organizations/${encodeURIComponent(repository.owner.login)}/settings/apps/new`
        : '/settings/apps/new';
      const action = `https://github.com${ownerPath}?state=${state}`;
      const nonce = randomBytes(16).toString('base64');
      response.setHeader('Content-Security-Policy', `default-src 'none'; script-src 'nonce-${nonce}'; form-action https://github.com; frame-ancestors 'none'`);
      response.end(`<meta charset="utf-8"><title>Opening GitHub</title><p>Opening GitHub to review and create the Renovate App for ${escapeHtml(repository.full_name)}.</p><form id="registration" method="post" action="${action}"><input type="hidden" name="manifest" value="${escapeHtml(JSON.stringify(manifest))}"><button type="submit">Continue to GitHub</button></form><script nonce="${nonce}">document.getElementById('registration').submit();</script>`);
      return;
    }
    const value = url.searchParams.get('code') || '';
    if (url.pathname !== '/callback' || !/^[a-zA-Z0-9_-]{16,256}$/.test(value) || consumed) {
      response.writeHead(400).end('The registration result is invalid.');
      return;
    }
    consumed = true;
    clearTimeout(timer);
    response.end('<meta charset="utf-8"><title>App registration received</title><p>Return to your terminal. The installation page will open next.</p>');
    complete(value);
  });
  await new Promise((resolveListening, reject) => {
    server.once('error', () => reject(new SetupError('Could not start the local callback listener.')));
    server.listen(0, '127.0.0.1', resolveListening);
  });
  origin = `http://127.0.0.1:${server.address().port}`;
  timer = setTimeout(() => {
    fail(new SetupError('App registration timed out.'));
    server.close();
  }, timeoutMs);
  return {
    url: `${origin}/?state=${state}`,
    code,
    cancel() { fail(new SetupError('App registration was cancelled.')); },
    close() { clearTimeout(timer); server.close(); server.closeAllConnections(); },
  };
}

async function openBrowser(url) {
  const [command, args] = process.platform === 'win32'
    ? ['rundll32.exe', ['url.dll,FileProtocolHandler', url]]
    : process.platform === 'darwin' ? ['open', [url]] : ['xdg-open', [url]];
  const opened = await new Promise(resolveOpened => {
    const child = spawn(command, args, { windowsHide: true, stdio: 'ignore' });
    child.once('error', () => resolveOpened(false));
    child.once('close', code => resolveOpened(code === 0));
  });
  if (!opened) console.log(`Open this URL in your browser: ${url}`);
}

async function github(path, { token, method = 'GET', body } = {}) {
  let response;
  try {
    response = await fetch(`https://api.github.com${path}`, {
      method,
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'UnityTemplate-Renovate-Setup',
        'X-GitHub-Api-Version': '2026-03-10',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: AbortSignal.timeout(30000),
    });
  } catch {
    throw new SetupError('Could not connect to the GitHub API.');
  }
  // The manifest conversion response contains the newly generated private key.
  if (!response.ok) return { status: response.status };
  return { status: response.status, data: response.status === 204 ? null : await response.json() };
}

export function appJwt(app, now = Math.floor(Date.now() / 1000)) {
  const header = Buffer.from(JSON.stringify({ alg: 'RS256', typ: 'JWT' })).toString('base64url');
  const payload = Buffer.from(JSON.stringify({ iat: now - 60, exp: now + 540, iss: app.client_id })).toString('base64url');
  const unsigned = `${header}.${payload}`;
  return `${unsigned}.${sign('RSA-SHA256', Buffer.from(unsigned), app.pem).toString('base64url')}`;
}

export async function registerCredentials(app, repository, runGh = gh) {
  await runGh(['secret', 'set', privateKeyName, '--repo', repository.full_name], app.pem);
  await runGh(['variable', 'set', clientIdName, '--repo', repository.full_name], app.client_id);
}

export async function waitForInstallation(app, repository, permissions, request = github, wait = ms => new Promise(resolveWait => setTimeout(resolveWait, ms)), attempts = 300) {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const result = await request(`/repos/${repository.full_name}/installation`, { token: appJwt(app) });
    if (result.status === 200) {
      if (result.data.account?.login?.toLowerCase() !== repository.owner.login.toLowerCase()) {
        throw new SetupError('The app installation does not belong to the repository owner.');
      }
      const missing = Object.entries(permissions).filter(([name, access]) => {
        const granted = result.data.permissions?.[name];
        return granted !== access && !(access === 'read' && granted === 'write');
      });
      if (missing.length) throw new SetupError(`The app installation is missing required permissions: ${missing.map(([name]) => name).join(', ')}.`);
      return;
    }
    if (result.status !== 404) throw new SetupError(`App installation verification failed (HTTP ${result.status}).`);
    await wait(2000);
  }
  throw new SetupError('The app installation was not confirmed. Check repository selection in the app settings.');
}

export async function main(args = process.argv.slice(2)) {
  const { values } = parseArgs({ args, options: {
    repo: { type: 'string' }, name: { type: 'string' }, help: { type: 'boolean' },
  } });
  if (values.help) {
    console.log('node .github/scripts/setup-renovate.mjs [--repo OWNER/REPO] [--name APP_NAME]');
    return;
  }
  const repositoryName = values.repo || (await gh(['repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner'])).trim();
  if (!/^[\w.-]+\/[\w.-]+$/.test(repositoryName)) throw new SetupError('Specify the target repository as OWNER/REPO.');
  const repository = JSON.parse(await gh(['api', `repos/${repositoryName}`, '--jq', '{name,full_name,owner:{login:.owner.login,type:.owner.type},permissions:{admin:.permissions.admin}}']));
  if (!repository.permissions?.admin) throw new SetupError('Sign in to GitHub CLI as an administrator of the target repository.');
  const secrets = JSON.parse(await gh(['secret', 'list', '--repo', repositoryName, '--json', 'name']));
  const variables = JSON.parse(await gh(['variable', 'list', '--repo', repositoryName, '--json', 'name']));
  if (secrets.some(item => item.name === privateKeyName) || variables.some(item => item.name === clientIdName)) {
    throw new SetupError('Renovate App credentials already exist and were not overwritten. Check the existing app installation and workflow results.');
  }
  const base = JSON.parse(await readFile(new URL('./setup-renovate.template.json', import.meta.url), 'utf8'));
  const registration = await registrationPage(base, repository, values.name);
  const interrupt = () => { registration.cancel(); };
  process.once('SIGINT', interrupt);
  let code;
  try {
    console.log(`${repository.full_name}: create the GitHub App in your browser.`);
    await openBrowser(registration.url);
    code = await registration.code;
  } finally {
    registration.close();
    process.removeListener('SIGINT', interrupt);
  }
  const conversion = await github(`/app-manifests/${code}/conversions`, { method: 'POST' });
  const app = conversion?.data;
  if (conversion?.status !== 201 || !app?.pem || !app.client_id || !/^[\w-]+$/.test(app.slug || '')) {
    throw new SetupError('Could not retrieve the registered app. Check your GitHub App settings.');
  }
  console.log(`App settings: https://github.com/apps/${app.slug}`);
  try {
    await registerCredentials(app, repository);
    console.log('App credentials are registered. Approve installation on the target repository in your browser.');
    await openBrowser(`https://github.com/apps/${app.slug}/installations/new`);
    const permissions = buildManifest(base, repository, '', values.name).default_permissions;
    await waitForInstallation(app, repository, permissions);
    await gh(['api', '--method', 'PUT', `repos/${repository.full_name}/vulnerability-alerts`]);
    await gh(['api', `repos/${repository.full_name}/vulnerability-alerts`]);
    console.log('Verified the app installation, registered credentials, and enabled vulnerability alerts and the dependency graph.');
  } finally {
    app.pem = null;
  }
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  main().catch(error => {
    console.error(error instanceof SetupError ? error.message : 'Setup failed. Check your GitHub App settings and registered credentials.');
    process.exitCode = 1;
  });
}
