import assert from 'node:assert/strict';
import { generateKeyPairSync, verify } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { request } from 'node:http';
import test from 'node:test';
import {
  SetupError, appJwt, buildManifest, registerCredentials, registrationPage, waitForInstallation,
} from './setup-renovate.mjs';

const base = JSON.parse(await readFile(new URL('./setup-renovate.template.json', import.meta.url), 'utf8'));
const repository = { name: 'project', full_name: 'example/project', owner: { login: 'example', type: 'User' } };
const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
const app = { client_id: 'test-client', pem: privateKey.export({ type: 'pkcs1', format: 'pem' }) };

function http(url, headers = {}) {
  return new Promise((resolveResponse, reject) => {
    const req = request(url, { headers }, response => {
      let body = '';
      response.setEncoding('utf8').on('data', chunk => { body += chunk; });
      response.on('end', () => resolveResponse({ status: response.statusCode, headers: response.headers, body }));
    });
    req.on('error', reject);
    req.end();
  });
}

test('registration rejects forged callbacks, unicode states, and foreign Host headers without consuming the real callback', async () => {
  const page = await registrationPage(base, repository, 'Example App');
  try {
    const start = new URL(page.url);
    const callback = new URL('/callback', start);
    const code = 'test-registration-code';
    callback.searchParams.set('code', code);
    callback.searchParams.set('state', 'forged');
    assert.equal((await http(callback)).status, 403);
    callback.searchParams.set('state', 'é'.repeat(64));
    assert.equal((await http(callback)).status, 403);
    callback.searchParams.set('state', start.searchParams.get('state'));
    assert.equal((await http(callback, { Host: 'attacker.invalid' })).status, 403);
    const result = await http(callback);
    assert.equal(result.status, 200);
    assert.equal(result.headers['cache-control'], 'no-store');
    assert.ok(!result.body.includes(code));
    assert.equal(await page.code, code);
    assert.equal((await http(callback)).status, 400);
  } finally { page.close(); }
});

test('the local registration form escapes untrusted app names and only posts to GitHub', async () => {
  const page = await registrationPage(base, repository, '\"><script>bad()</script>');
  try {
    const result = await http(page.url);
    assert.equal(result.status, 200);
    assert.ok(!result.body.includes('<script>'));
    assert.ok(result.body.includes('&lt;script&gt;'));
    assert.match(result.body, /action="https:\/\/github\.com\/settings\/apps\/new\?state=/);
    assert.match(result.headers['content-security-policy'], /form-action https:\/\/github\.com/);
    const nonce = result.body.match(/<script nonce="([^"]+)">/)[1];
    assert.ok(result.headers['content-security-policy'].includes(`script-src 'nonce-${nonce}'`));
    assert.ok(result.body.includes("document.getElementById('registration').submit()"));
  } finally { page.close(); }
});

test('registration timeout and cancellation reject rather than report completion', async () => {
  const timed = await registrationPage(base, repository, 'Example', 20);
  try { await assert.rejects(timed.code, /timed out/); } finally { timed.close(); }
  const cancelled = await registrationPage(base, repository, 'Example');
  try {
    cancelled.cancel();
    await assert.rejects(cancelled.code, /cancelled/);
  } finally { cancelled.close(); }
});

test('organization permissions are scoped per generated manifest without modifying the shared template', () => {
  const organization = { ...repository, full_name: 'team/project', owner: { login: 'team', type: 'Organization' } };
  const orgManifest = buildManifest(base, organization, 'http://127.0.0.1/callback');
  assert.equal(orgManifest.default_permissions.members, 'read');
  assert.equal(orgManifest.default_permissions.administration, 'read');
  assert.equal(orgManifest.default_permissions.vulnerability_alerts, 'read');
  assert.equal(orgManifest.default_permissions.workflows, 'write');
  assert.equal(orgManifest.url, 'https://github.com/team/project');
  assert.equal(base.url, 'https://github.com/{{repository}}');
  assert.equal(buildManifest(base, repository, 'http://127.0.0.1/callback').default_permissions.members, undefined);
  assert.equal(base.default_permissions.members, undefined);
});

test('app authentication uses a verifiable RSA signature with a bounded lifetime and clock skew allowance', () => {
  const jwt = appJwt(app, 1000);
  const [header, payload, signature] = jwt.split('.');
  assert.ok(verify('RSA-SHA256', Buffer.from(`${header}.${payload}`), publicKey, Buffer.from(signature, 'base64url')));
  const claims = JSON.parse(Buffer.from(payload, 'base64url'));
  assert.equal(claims.iss, app.client_id);
  assert.ok(claims.iat <= 1000 && claims.exp > 1000 && claims.exp - claims.iat <= 600);
});

test('private keys are sent through stdin and never included in command arguments', async () => {
  const calls = [];
  await registerCredentials(app, repository, async (args, input) => { calls.push({ args, input }); });
  assert.equal(calls[0].input, app.pem);
  assert.equal(calls[1].input, app.client_id);
  for (const call of calls) assert.ok(!call.args.join(' ').includes(app.pem));
  assert.ok(calls.every(call => call.args.includes(repository.full_name)));
});

test('a failed secret registration stops before writing the public configuration', async () => {
  let calls = 0;
  await assert.rejects(registerCredentials(app, repository, async () => {
    calls++;
    throw new SetupError('registration failed');
  }), /registration failed/);
  assert.equal(calls, 1);
});

test('installation verification waits for repository access and rejects another owner', async () => {
  let calls = 0;
  await waitForInstallation(app, repository, base.default_permissions, async () => ++calls === 1
    ? { status: 404 }
    : { status: 200, data: { account: { login: 'EXAMPLE' }, permissions: base.default_permissions } }, async () => {}, 2);
  assert.equal(calls, 2);
  await assert.rejects(waitForInstallation(app, repository, base.default_permissions, async () => ({
    status: 200, data: { account: { login: 'other-owner' } },
  })), /does not belong/);
  await assert.rejects(waitForInstallation(app, repository, base.default_permissions, async () => ({ status: 404 }), async () => {}, 1), /not confirmed/);
});

test('installation verification rejects insufficient workflow permissions', async () => {
  await assert.rejects(waitForInstallation(app, repository, base.default_permissions, async () => ({
    status: 200,
    data: { account: { login: repository.owner.login }, permissions: { ...base.default_permissions, workflows: 'read' } },
  })), /missing required permissions: workflows/);
});
