import { describe, it, expect, vi, beforeEach } from 'vitest';
import worker, { ReportCoordinator } from './index';
import { validate, issueBody } from './schema';

const report = () => ({ Schema: 1, IncidentId: crypto.randomUUID(), AppVersion: '1.0.0.0', Build: 'a'.repeat(64),
  WindowsVersion: '10.0.26100.0', LaunchMethod: 'Steam', Action: 'Restart', Outcome: 'failed',
  Events: [{ Time: '2026-10-10T18:00:00.0000000Z', Stage: 'termination', Outcome: 'survived', Pid: 123, Error: 5, Created: 0, Milliseconds: 6000 }] });
class Storage {
  values = new Map<string, unknown>();
  async get<T>(k: string) { return this.values.get(k) as T; }
  async put(k: string | Record<string, unknown>, v?: unknown) { if (typeof k === 'string') this.values.set(k, v); else for (const [key, value] of Object.entries(k)) this.values.set(key, value); }
  async setAlarm() {}
  async list() { return this.values; }
  async delete(keys: string[]) { for (const k of keys) this.values.delete(k); }
}
let env: any;
beforeEach(async () => {
  vi.restoreAllMocks();
  const keys = await crypto.subtle.generateKey({ name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['sign', 'verify']);
  const pem = Buffer.from(await crypto.subtle.exportKey('pkcs8', keys.privateKey)).toString('base64');
  env = { REPORTING_ENABLED: 'true', GITHUB_APP_ID: '1', GITHUB_INSTALLATION_ID: '2', GITHUB_PRIVATE_KEY: `-----BEGIN PRIVATE KEY-----\n${pem}\n-----END PRIVATE KEY-----`, IP_HASH_KEY: 'test-secret' };
});
const req = (r: unknown, source = 'b'.repeat(64)) => new Request('https://internal/reports', { method: 'POST', headers: { 'X-Source': source }, body: JSON.stringify(r) });
function coordinator() { const storage = new Storage(); return { storage, coordinator: new ReportCoordinator({ storage } as any, env) }; }
function mockGithub(create: () => Promise<Response>) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (url) => {
    if (String(url).includes('/access_tokens')) return Response.json({ token: 'test-installation-token' }, { status: 201 });
    return create();
  });
}
describe('schema and privacy', () => {
  it('accepts controlled diagnostics and constructs a fixed issue body', () => {
    const r = report(); expect(validate(r).IncidentId).toBe(r.IncidentId); expect(issueBody(validate(r))).toContain(`fhqa-incident:${r.IncidentId}`);
  });
  it.each(['Username', 'Path', 'CommandLine', 'Repository', 'issueUrl'])('rejects arbitrary field %s', field => {
    expect(() => validate({ ...report(), [field]: 'private' })).toThrow();
  });
  it('rejects arbitrary diagnostic strings and oversized event lists', () => {
    const r = report(); r.Events[0].Outcome = 'C:\\Users\\secret'; expect(() => validate(r)).toThrow();
    const many = report(); many.Events = Array(201).fill(many.Events[0]); expect(() => validate(many)).toThrow();
  });
  it('rejects malformed and oversized streamed bodies without forwarding', async () => {
    const request = (body: string) => new Request('https://worker/reports', { method: 'POST', headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '192.0.2.1' }, body });
    expect((await worker.fetch(request('{'), env)).status).toBe(400);
    expect((await worker.fetch(request(' '.repeat(32769)), env)).status).toBe(413);
  });
  it('honors the kill switch', async () => {
    expect((await worker.fetch(new Request('https://worker/reports', { method: 'POST' }), { ...env, REPORTING_ENABLED: 'false' })).status).toBe(503);
  });
});
describe('submission durability and limits', () => {
  it('creates once and returns the stored URL on retry', async () => {
    let creates = 0; mockGithub(async () => { creates++; return Response.json({ html_url: 'https://github.com/j117mw3/ForHonorQuickActions/issues/1' }, { status: 201 }); });
    const { coordinator: c } = coordinator(); const r = report();
    expect((await c.fetch(req(r))).status).toBe(200); expect((await c.fetch(req(r))).status).toBe(200); expect(creates).toBe(1);
    expect((await c.fetch(req({ ...r, Outcome: 'helper-timeout' }))).status).toBe(409);
  });
  it('serializes concurrent duplicate submissions', async () => {
    let creates = 0; mockGithub(async () => { creates++; return Response.json({ html_url: 'https://github.com/j117mw3/ForHonorQuickActions/issues/2' }, { status: 201 }); });
    const { coordinator: c } = coordinator(); const r = report(); await Promise.all([c.fetch(req(r)), c.fetch(req(r))]); expect(creates).toBe(1);
  });
  it('enforces three per source and the global daily cap', async () => {
    mockGithub(async () => Response.json({ html_url: 'https://github.com/j117mw3/ForHonorQuickActions/issues/3' }, { status: 201 }));
    const { coordinator: c, storage } = coordinator();
    for (let i = 0; i < 3; i++) expect((await c.fetch(req(report()))).status).toBe(200);
    expect((await c.fetch(req(report()))).status).toBe(429);
    storage.values.set(`day:${Math.floor(Date.now() / 86400000)}`, 100);
    expect((await c.fetch(req(report(), 'c'.repeat(64)))).status).toBe(429);
  });
  it('reconciles a response lost after GitHub created the issue', async () => {
    const r = report(); let creates = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (url) => {
      if (String(url).includes('/access_tokens')) return Response.json({ token: 'token' }, { status: 201 });
      if (String(url).includes('?')) return Response.json([{ body: issueBody(validate(r)), html_url: 'https://github.com/j117mw3/ForHonorQuickActions/issues/4' }]);
      creates++; throw new Error('connection lost');
    });
    const { coordinator: c } = coordinator(); expect((await c.fetch(req(r))).status).toBe(503);
    expect((await c.fetch(req(r))).status).toBe(200); expect(creates).toBe(1);
  });
  it('never recreates an ambiguous submission without a matching issue', async () => {
    let creates = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (url) => {
      if (String(url).includes('/access_tokens')) return Response.json({ token: 'token' }, { status: 201 });
      if (String(url).includes('?')) return Response.json([]);
      creates++; return new Response('', { status: 500 });
    });
    const { coordinator: c } = coordinator(); const r = report();
    expect((await c.fetch(req(r))).status).toBe(503); expect((await c.fetch(req(r))).status).toBe(503); expect(creates).toBe(1);
  });
  it('allows retry after a definite GitHub rejection', async () => {
    let creates = 0; mockGithub(async () => { creates++; return creates === 1 ? new Response('', { status: 403 }) : Response.json({ html_url: 'https://github.com/j117mw3/ForHonorQuickActions/issues/5' }, { status: 201 }); });
    const { coordinator: c } = coordinator(); const r = report(); expect((await c.fetch(req(r))).status).toBe(503); expect((await c.fetch(req(r))).status).toBe(200);
  });
});
