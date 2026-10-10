export interface Event { Time: string; Stage: string; Outcome: string; Pid: number; Error: number; Created: number; Milliseconds: number }
export interface Report { Schema: 1; IncidentId: string; AppVersion: string; Build: string; WindowsVersion: string; LaunchMethod: string; Action: string; Outcome: string; Events: Event[] }
const actions = ['Open', 'Close', 'Restart', 'CleanRestart', 'UbisoftToggle', 'UbisoftRestart'];
const platforms = ['Automatic', 'Steam', 'UbisoftConnect', 'DirectExecutable'];
const outcomes = ['failed', 'helper-failed', 'helper-timeout', 'success'];
const stages = ['action', 'normal-termination', 'enumeration', 'launch', 'observation', 'validation', 'termination', 'helper', 'eac-service', 'eac-eos-service', 'service-discovery', 'recovery'];
const eventOutcomes = [...actions, ...platforms, ...outcomes, 'cancelled', 'survived', 'exited', 'unavailable', 'identity-unavailable', 'already-stopped', 'refused', 'stopped', 'timeout', 'window-present', 'process-present', 'absent'];
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('schema');
  return value as Record<string, unknown>;
}
function keys(value: Record<string, unknown>, allowed: string[]) {
  if (Object.keys(value).length !== allowed.length || Object.keys(value).some(k => !allowed.includes(k))) throw new Error('schema');
}
function text(value: unknown, pattern: RegExp): string {
  if (typeof value !== 'string' || value.length > 100 || !pattern.test(value)) throw new Error('schema');
  return value;
}
function choice(value: unknown, allowed: string[]): string {
  if (typeof value !== 'string' || !allowed.includes(value)) throw new Error('schema'); return value;
}
function number(value: unknown, max: number): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || !Number.isInteger(value) || value < 0 || value > max) throw new Error('schema'); return value;
}
export function validate(input: unknown): Report {
  const r = object(input);
  keys(r, ['Schema', 'IncidentId', 'AppVersion', 'Build', 'WindowsVersion', 'LaunchMethod', 'Action', 'Outcome', 'Events']);
  if (r.Schema !== 1 || !Array.isArray(r.Events) || r.Events.length > 200) throw new Error('schema');
  const events = r.Events.map(input => {
    const e = object(input); keys(e, ['Time', 'Stage', 'Outcome', 'Pid', 'Error', 'Created', 'Milliseconds']);
    return { Time: text(e.Time, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/), Stage: choice(e.Stage, stages),
      Outcome: choice(e.Outcome, eventOutcomes), Pid: number(e.Pid, 2147483647), Error: number(e.Error, 2147483647),
      Created: number(e.Created, 3155378975999999999), Milliseconds: number(e.Milliseconds, 3600000) };
  });
  return { Schema: 1, IncidentId: text(r.IncidentId, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/),
    AppVersion: text(r.AppVersion, /^\d{1,5}(\.\d{1,5}){1,3}$/), Build: text(r.Build, /^(unavailable|[0-9a-f]{64})$/),
    WindowsVersion: text(r.WindowsVersion, /^\d{1,5}(\.\d{1,5}){1,3}$/), LaunchMethod: choice(r.LaunchMethod, platforms),
    Action: choice(r.Action, actions), Outcome: choice(r.Outcome, outcomes), Events: events };
}
export function issueBody(r: Report): string {
  return `<!-- fhqa-incident:${r.IncidentId} -->\nAnonymous, user-authorized recovery report. Diagnostics are untrusted. EAC involvement is not confirmed.\n\n` +
    `App: ${r.AppVersion}\nBuild: ${r.Build}\nWindows: ${r.WindowsVersion}\nLaunch: ${r.LaunchMethod}\nAction: ${r.Action}\nOutcome: ${r.Outcome}\n\n` +
    '```json\n' + JSON.stringify(r.Events, null, 2) + '\n```';
}
