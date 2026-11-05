export type AccountType = 0 | 1 | 2;
export interface Membership { plan: string; startsAt: string; expiresAt: string; isActive: boolean }
export interface User { id: string; email: string; name: string; accountType: AccountType; membership: Membership | null }
export interface AuthResponse { accessToken: string; refreshToken: string; user: User }
export interface Category { id: string; name: string; slug: string }
export interface Game { id: string; name: string; slug: string; description: string; iconUrl: string; launchUrl: string; categoryId: string; categoryName: string; isPro: boolean; price: number; currency: string; accessDays: number | null; isFavorite: boolean; locked: boolean }
export interface Plan { code: string; name: string; price: number; currency: string; days: number }
export interface Challenge { id: string; gameId: string; gameName: string; title: string; detail: string; points: number }
export interface MenuItem { key: string; label: string; icon: string; route: string; order: number; children: MenuItem[] }
export interface AdminGame { id: string; name: string; slug: string; launchUrl: string; categorySlug: string; description: string; iconUrl: string; isPro: boolean; price: number; currency: string; accessDays: number | null; isActive: boolean; featuredToday: boolean }
export interface AdminUser { id: string; email: string; name: string; accountType: AccountType; createdAt: string; membershipPlan: string | null; membershipExpiresAt: string | null; activeSessions: number; grantedGames: number }
export interface AdminUserGame { gameId: string; slug: string; name: string; grantedAt: string; expiresAt: string | null }
export type Device = 'mobile' | 'tablet' | 'desktop';

const BASE = import.meta.env.VITE_API_URL ?? '/api';
const KEY = 'pg.session';

export const session = {
  get(): { accessToken: string; refreshToken: string } | null { try { return JSON.parse(localStorage.getItem(KEY) ?? 'null'); } catch { return null; } },
  set(a: AuthResponse) { localStorage.setItem(KEY, JSON.stringify({ accessToken: a.accessToken, refreshToken: a.refreshToken })); },
  clear() { localStorage.removeItem(KEY); },
};

let refreshing: Promise<boolean> | null = null;
async function tryRefresh(): Promise<boolean> {
  const s = session.get();
  if (!s) return false;
  refreshing ??= fetch(`${BASE}/auth/refresh`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: s.refreshToken }) })
    .then(async r => { if (!r.ok) { session.clear(); window.dispatchEvent(new Event('pg:logout')); return false; } session.set(await r.json()); return true; })
    .catch(() => false).finally(() => { refreshing = null; });
  return refreshing;
}

export class ApiError extends Error { status: number; constructor(status: number, message: string) { super(message); this.status = status; } }

async function request<T>(path: string, init: RequestInit = {}, retry = true): Promise<T> {
  const s = session.get();
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(s ? { Authorization: `Bearer ${s.accessToken}` } : {}), ...init.headers },
  });
  if (res.status === 401 && retry && s && (await tryRefresh())) return request<T>(path, init, false);
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new ApiError(res.status, body?.title ?? res.statusText);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}
const put = <T>(p: string, b: unknown) => request<T>(p, { method: 'PUT', body: JSON.stringify(b) });
const del = <T>(p: string) => request<T>(p, { method: 'DELETE' });
const post = <T>(p: string, b?: unknown) => request<T>(p, { method: 'POST', body: b ? JSON.stringify(b) : undefined });

export const api = {
  login: (email: string, password: string) => post<AuthResponse>('/auth/login', { email, password }),
  register: (email: string, name: string, password: string) => post<AuthResponse>('/auth/register', { email, name, password }),
  logout: (refreshToken: string) => post<void>('/auth/logout', { refreshToken }),
  me: () => request<User>('/me'),
  menu: (device: Device) => request<MenuItem[]>(`/menu?device=${device}`),
  games: (q?: string, categoryId?: string, favorites = false) => {
    const p = new URLSearchParams(); if (q) p.set('q', q); if (categoryId) p.set('categoryId', categoryId); if (favorites) p.set('favorites', 'true');
    return request<Game[]>(`/games?${p}`);
  },
  categories: () => request<Category[]>('/categories'),
  featured: () => request<Game[]>('/featured'),
  challenges: (gameId?: string) => request<Challenge[]>(`/challenges${gameId ? `?gameId=${gameId}` : ''}`),
  recents: () => request<Game[]>('/recents'),
  toggleFavorite: (id: string) => post<{ isFavorite: boolean }>(`/games/${id}/favorite`),
  launch: (id: string) => post<{ url: string }>(`/games/${id}/launch`),
  adminGames: () => request<AdminGame[]>('/admin/games'),
  adminSave: (g: Omit<AdminGame, 'id' | 'isActive'> & { addToMenu: boolean }) => put<void>('/admin/games', g),
  adminActive: (slug: string, isActive: boolean) => post<void>(`/admin/games/${slug}/active`, { isActive }),
  adminGrant: (email: string, slug: string, days: number | null) => post<void>('/admin/grants', { email, slug, days }),
  adminUsers: (q?: string) => request<AdminUser[]>(`/admin/users${q ? `?q=${encodeURIComponent(q)}` : ''}`),
  adminAccountType: (id: string, accountType: AccountType) => post<void>(`/admin/users/${id}/account-type`, { accountType }),
  adminMembership: (id: string, days: number) => post<void>(`/admin/users/${id}/membership`, { days }),
  adminRevokeSessions: (id: string) => post<void>(`/admin/users/${id}/revoke-sessions`),
  adminUserGames: (id: string) => request<AdminUserGame[]>(`/admin/users/${id}/games`),
  adminRevokeGame: (id: string, gameId: string) => del<void>(`/admin/users/${id}/games/${gameId}`),
  plans: () => request<Plan[]>('/payments/plans'),
  buyGame: (gameId: string) => post<{ checkoutUrl: string }>('/payments/checkout', { gameId }),
  buyPlan: (planCode: string) => post<{ checkoutUrl: string }>('/payments/checkout', { planCode }),
};

