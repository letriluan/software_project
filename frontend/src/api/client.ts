import type { AuthResponse } from './types';

/** RFC 9457 problem details returned by the API. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly fieldErrors: Record<string, string[]>;
  readonly body: Record<string, unknown>;

  constructor(status: number, body: Record<string, unknown>) {
    const detail = typeof body.detail === 'string' ? body.detail : undefined;
    const title = typeof body.title === 'string' ? body.title : `Request failed (${status})`;
    super(detail ?? title);
    this.status = status;
    this.title = title;
    this.body = body;
    this.fieldErrors = (body.errors as Record<string, string[]> | undefined) ?? {};
  }

  /** First error for a field, matching the server's PascalCase or our camelCase key. */
  field(name: string): string | undefined {
    const pascal = name.charAt(0).toUpperCase() + name.slice(1);
    return (this.fieldErrors[name] ?? this.fieldErrors[pascal])?.[0];
  }
}

// The access token lives only in memory. The refresh token is an httpOnly cookie the
// browser sends to /api/auth/* automatically, so a page reload can silently restore the session.
let accessToken: string | null = null;
let onSessionExpired: (() => void) | null = null;
let refreshing: Promise<AuthResponse | null> | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function setSessionExpiredHandler(handler: (() => void) | null) {
  onSessionExpired = handler;
}

/** Single-flight refresh: concurrent 401s share one refresh call. */
export function refreshSession(): Promise<AuthResponse | null> {
  refreshing ??= fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' })
    .then(async (res) => {
      if (!res.ok) return null;
      const body = (await res.json()) as AuthResponse;
      accessToken = body.accessToken;
      return body;
    })
    .catch(() => null)
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

type Query = Record<string, string | number | boolean | null | undefined>;

export function withQuery(path: string, query?: Query): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  }
  const qs = params.toString();
  return qs ? `${path}?${qs}` : path;
}

async function send(method: string, path: string, body?: unknown): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  return fetch(path, {
    method,
    headers,
    credentials: 'same-origin',
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

export async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res = await send(method, path, body);

  if (res.status === 401 && !path.startsWith('/api/auth/')) {
    const refreshed = await refreshSession();
    if (refreshed) {
      res = await send(method, path, body);
    } else {
      onSessionExpired?.();
    }
  }

  if (!res.ok) {
    let problem: Record<string, unknown> = {};
    try {
      problem = (await res.json()) as Record<string, unknown>;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, problem);
  }

  if (res.status === 204) return undefined as T;
  const type = res.headers.get('Content-Type') ?? '';
  return (type.includes('json') ? await res.json() : await res.text()) as T;
}

export const api = {
  get: <T>(path: string, query?: Query) => request<T>('GET', withQuery(path, query)),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  delete: <T>(path: string) => request<T>('DELETE', path),
};

/** Downloads a file from an authenticated endpoint. */
export async function download(path: string, filename: string) {
  let res = await send('GET', path);
  if (res.status === 401 && (await refreshSession())) res = await send('GET', path);
  if (!res.ok) throw new ApiError(res.status, {});
  const url = URL.createObjectURL(await res.blob());
  const a = Object.assign(document.createElement('a'), { href: url, download: filename });
  a.click();
  URL.revokeObjectURL(url);
}
