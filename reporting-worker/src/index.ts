import { validate, issueBody, type Report } from './schema';
interface Env {
  REPORTS: DurableObjectNamespace; REPORTING_ENABLED: string; GITHUB_APP_ID: string;
  GITHUB_INSTALLATION_ID: string; GITHUB_PRIVATE_KEY: string; IP_HASH_KEY: string;
}
interface Submission { hash: string; created: number; status: 'pending' | 'sent' | 'rejected'; attempts: number; issueUrl?: string }
const repo = 'j117mw3/ForHonorQuickActions';
const response = (status: number, body: object) => Response.json(body, { status, headers: { 'Cache-Control': 'no-store' } });
function bytesToHex(bytes: ArrayBuffer) { return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join(''); }
async function digest(text: string) { return bytesToHex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))); }
async function sourceHash(ip: string, secret: string) {
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return bytesToHex(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(ip)));
}
async function boundedBody(req: Request): Promise<string> {
  if (!req.body) throw new Error('body');
  const reader = req.body.getReader(); const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) { const { done, value } = await reader.read(); if (done) break; size += value.length; if (size > 32768) throw new Error('size'); chunks.push(value); }
  } finally { await reader.cancel(); }
  const bytes = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
}
export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    if (new URL(req.url).pathname !== '/reports') return response(404, { status: 'unavailable' });
    if (req.method !== 'POST') return response(405, { status: 'method' });
    if (env.REPORTING_ENABLED !== 'true' || !env.IP_HASH_KEY) return response(503, { status: 'disabled' });
    if (!/^application\/json(?:;|$)/i.test(req.headers.get('Content-Type') ?? '')) return response(415, { status: 'format' });
    let report: Report;
    try { report = validate(JSON.parse(await boundedBody(req))); }
    catch (error) { return response(error instanceof Error && error.message === 'size' ? 413 : 400, { status: 'invalid' }); }
    const ip = req.headers.get('CF-Connecting-IP');
    if (!ip) return response(400, { status: 'source-unavailable' });
    try {
      const source = await sourceHash(ip, env.IP_HASH_KEY);
      return await env.REPORTS.get(env.REPORTS.idFromName('coordinator')).fetch('https://internal/reports', {
        method: 'POST', headers: { 'X-Source': source }, body: JSON.stringify(report) });
    } catch { return response(503, { status: 'unavailable' }); }
  }
};
export class ReportCoordinator {
  private tail: Promise<unknown> = Promise.resolve();
  constructor(private state: DurableObjectState, private env: Env) {}
  async fetch(req: Request): Promise<Response> {
    const work = this.tail.then(() => this.submit(req));
    this.tail = work.catch(() => undefined);
    try { return await work; } catch { return response(503, { status: 'unavailable' }); }
  }
  private async submit(req: Request): Promise<Response> {
    if (this.env.REPORTING_ENABLED !== 'true') return response(503, { status: 'disabled' });
    const r = validate(await req.json()); const now = Date.now(); const source = req.headers.get('X-Source');
    if (!source || !/^[0-9a-f]{64}$/.test(source)) return response(400, { status: 'invalid' });
    const key = 'incident:' + r.IncidentId; const hash = await digest(JSON.stringify(r));
    let record = await this.state.storage.get<Submission>(key);
    if (record && record.hash !== hash) return response(409, { status: 'conflict' });
    if (record?.status === 'sent') return response(200, { status: 'sent', issueUrl: record.issueUrl });
    if (record?.status === 'rejected' && record.attempts >= 3) return response(429, { status: 'limited' });
    // Reject exhausted quota before requesting another GitHub installation token.
    if (!record) {
      const count = (await this.state.storage.get<number>(`limit:${Math.floor(now / 3600000)}:${source}`)) ?? 0;
      const global = (await this.state.storage.get<number>(`day:${Math.floor(now / 86400000)}`)) ?? 0;
      if (count >= 3 || global >= 100) return response(429, { status: 'limited' });
    }
    // Authenticate before reserving quota or recording a submission.
    const token = await installationToken(this.env);
    if (record?.status === 'pending') {
      const url = await reconcile(token, r.IncidentId, record.created);
      if (!url) return response(503, { status: 'pending' });
      record.status = 'sent'; record.issueUrl = url; await this.state.storage.put(key, record);
      return response(200, { status: 'sent', issueUrl: url });
    }
    if (!record) {
      const hour = Math.floor(now / 3600000); const day = Math.floor(now / 86400000);
      const sourceKey = `limit:${hour}:${source}`; const dayKey = `day:${day}`;
      const count = (await this.state.storage.get<number>(sourceKey)) ?? 0;
      const global = (await this.state.storage.get<number>(dayKey)) ?? 0;
      if (count >= 3 || global >= 100) return response(429, { status: 'limited' });
      record = { hash, created: now, status: 'pending', attempts: 1 };
      await this.state.storage.put({ [sourceKey]: count + 1, [dayKey]: global + 1, [key]: record });
      await this.state.storage.setAlarm(now + 3600000);
    } else { record.status = 'pending'; record.attempts++; await this.state.storage.put(key, record); }
    const result = await github(`/repos/${repo}/issues`, token, { method: 'POST', body: JSON.stringify({
      title: `Stuck For Honor recovery: ${r.Outcome}`, body: issueBody(r) }) });
    if (result.status !== 201) {
      // Definite rejection permits retry; ambiguous network/server outcomes stay pending and are reconciled only.
      if ([400, 401, 403, 404, 422, 429].includes(result.status)) { record.status = 'rejected'; await this.state.storage.put(key, record); }
      return response(503, { status: 'unavailable' });
    }
    const issue = await result.json() as { html_url: string };
    if (!validIssueUrl(issue.html_url)) return response(503, { status: 'pending' });
    record.status = 'sent'; record.issueUrl = issue.html_url; await this.state.storage.put(key, record);
    return response(200, { status: 'sent', issueUrl: record.issueUrl });
  }
  async alarm() {
    const now = Date.now();
    const entries = await this.state.storage.list(); const remove: string[] = [];
    for (const [key, value] of entries) {
      if (key.startsWith('limit:') && Number(key.split(':')[1]) < Math.floor(now / 3600000)) remove.push(key);
      if (key.startsWith('day:') && Number(key.split(':')[1]) < Math.floor(now / 86400000)) remove.push(key);
      // Keep sent and ambiguous submissions as tombstones: expiry could create duplicate issues on a late retry.
      if (key.startsWith('incident:') && (value as Submission).status === 'rejected' && (value as Submission).created < now - 30 * 86400000) remove.push(key);
    }
    for (let i = 0; i < remove.length; i += 128) await this.state.storage.delete(remove.slice(i, i + 128));
    if (entries.size > remove.length) await this.state.storage.setAlarm(now + 3600000);
  }
}
const validIssueUrl = (url: string) => /^https:\/\/github\.com\/j117mw3\/ForHonorQuickActions\/issues\/\d+$/.test(url);
async function github(path: string, token: string, init: RequestInit = {}) {
  return fetch('https://api.github.com' + path, { ...init, signal: AbortSignal.timeout(8000), headers: {
    Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'Content-Type': 'application/json',
    'User-Agent': 'ForHonorQuickActions-Reports', 'X-GitHub-Api-Version': '2022-11-28' } });
}
async function reconcile(token: string, id: string, created: number): Promise<string | undefined> {
  const marker = `<!-- fhqa-incident:${id} -->`;
  for (let page = 1; page <= 10; page++) {
    const result = await github(`/repos/${repo}/issues?state=all&sort=created&direction=desc&per_page=100&page=${page}&since=${encodeURIComponent(new Date(created - 60000).toISOString())}`, token);
    if (!result.ok) throw new Error('github');
    const issues = await result.json() as { body?: string; html_url: string }[];
    const found = issues.find(issue => issue.body?.includes(marker) && validIssueUrl(issue.html_url)); if (found) return found.html_url;
    if (issues.length < 100) break;
  }
  return undefined;
}
function base64url(bytes: Uint8Array) { return btoa(String.fromCharCode(...bytes)).replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_'); }
async function installationToken(env: Env): Promise<string> {
  if (!/^\d+$/.test(env.GITHUB_APP_ID) || !/^\d+$/.test(env.GITHUB_INSTALLATION_ID)) throw new Error('config');
  const pem = env.GITHUB_PRIVATE_KEY.replace(/-----BEGIN PRIVATE KEY-----|-----END PRIVATE KEY-----|\s/g, '');
  const key = await crypto.subtle.importKey('pkcs8', Uint8Array.from(atob(pem), c => c.charCodeAt(0)), { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['sign']);
  const now = Math.floor(Date.now() / 1000);
  const encode = (value: object) => base64url(new TextEncoder().encode(JSON.stringify(value)));
  const input = encode({ alg: 'RS256', typ: 'JWT' }) + '.' + encode({ iat: now - 60, exp: now + 540, iss: env.GITHUB_APP_ID });
  const jwt = input + '.' + base64url(new Uint8Array(await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key, new TextEncoder().encode(input))));
  const result = await github(`/app/installations/${env.GITHUB_INSTALLATION_ID}/access_tokens`, jwt, {
    method: 'POST', body: JSON.stringify({ repositories: ['ForHonorQuickActions'], permissions: { issues: 'write' } }) });
  if (!result.ok) throw new Error('github');
  const body = await result.json() as { token: string }; return body.token;
}
