const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5080';

export type PagedResult<T> = {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
};

export type ApplicationDto = {
  id: string;
  name: string;
  clientId: string;
  type: string;
  redirectUris: string[];
  grantTypes: string[];
};

export type UserDto = {
  id: string;
  email: string;
  displayName: string;
  emailVerified: boolean;
  isBlocked: boolean;
  createdAt: string;
};

export type AuditEventDto = {
  id: string;
  occurredAt: string;
  eventType: string;
  actorUserId?: string;
  actorClientId?: string;
  ipAddress?: string;
  userAgent?: string;
  dataJson: string;
};

export type LoginResponse = {
  userId: string;
  tenantId: string;
  email: string;
  displayName: string;
  requiresMfa: boolean;
  loginTicket?: string;
  accessToken?: string;
  idToken?: string;
};

export const session = {
  get token() {
    return localStorage.getItem('netauth0.access_token');
  },
  set token(value: string | null) {
    if (value) localStorage.setItem('netauth0.access_token', value);
    else localStorage.removeItem('netauth0.access_token');
  },
  get tenantId() {
    return localStorage.getItem('netauth0.tenant_id') ?? '';
  },
  set tenantId(value: string) {
    localStorage.setItem('netauth0.tenant_id', value);
  }
};

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (!headers.has('Content-Type') && init.body) headers.set('Content-Type', 'application/json');
  if (session.token) headers.set('Authorization', `Bearer ${session.token}`);

  const response = await fetch(`${API_URL}${path}`, { ...init, headers });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({ title: response.statusText }));
    throw new Error(problem.detail ?? problem.title ?? 'Request failed');
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export const api = {
  login: (body: { tenantSlug: string; email: string; password: string; totpCode?: string }) =>
    request<LoginResponse>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify(body) }),
  signup: (body: { tenantSlug: string; email: string; displayName: string; password: string }) =>
    request<{ id: string }>('/api/v1/auth/signup', { method: 'POST', body: JSON.stringify(body) }),
  applications: (tenantId: string) =>
    request<ApplicationDto[]>(`/api/v1/admin/applications?tenantId=${tenantId}`),
  createApplication: (body: unknown) =>
    request<{ clientId: string; clientSecret?: string }>('/api/v1/admin/applications', { method: 'POST', body: JSON.stringify(body) }),
  users: (tenantId: string, query: string) =>
    request<PagedResult<UserDto>>(`/api/v1/admin/users?tenantId=${tenantId}&q=${encodeURIComponent(query)}`),
  blockUser: (tenantId: string, userId: string, blocked: boolean) =>
    request<void>(`/api/v1/admin/users/${userId}/block?tenantId=${tenantId}&blocked=${blocked}`, { method: 'POST' }),
  forceLogout: (tenantId: string, userId: string) =>
    request<void>(`/api/v1/admin/users/${userId}/force-logout?tenantId=${tenantId}`, { method: 'POST' }),
  audit: (tenantId: string, eventType: string) =>
    request<PagedResult<AuditEventDto>>(`/api/v1/admin/audit-events?tenantId=${tenantId}&eventType=${encodeURIComponent(eventType)}`),
  tokenEndpoint: `${API_URL}/connect/token`,
  authorizeEndpoint: `${API_URL}/connect/authorize`
};
