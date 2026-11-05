import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, session, type User } from './api';

interface AuthCtx { user: User | null; loading: boolean; login(e: string, p: string): Promise<void>; register(e: string, n: string, p: string): Promise<void>; logout(): Promise<void>; refresh(): Promise<void> }
const Ctx = createContext<AuthCtx>(null!);
export const useAuth = () => useContext(Ctx);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    if (!session.get()) { setLoading(false); return; }
    api.me().then(setUser).catch(() => session.clear()).finally(() => setLoading(false));
  }, []);
  useEffect(() => { const f = () => setUser(null); window.addEventListener('pg:logout', f); return () => window.removeEventListener('pg:logout', f); }, []);
  const value: AuthCtx = {
    user, loading,
    async login(e, p) { const r = await api.login(e, p); session.set(r); setUser(r.user); },
    async register(e, n, p) { const r = await api.register(e, n, p); session.set(r); setUser(r.user); },
    async logout() { const s = session.get(); if (s) await api.logout(s.refreshToken).catch(() => {}); session.clear(); setUser(null); },
    async refresh() { setUser(await api.me()); },
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
