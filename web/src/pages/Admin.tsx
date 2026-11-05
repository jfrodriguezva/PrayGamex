import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, type AccountType, type AdminGame, type AdminUser, type AdminUserGame, type Category } from '../api';
import { useAuth } from '../auth';

const ACCOUNT = ['Gratuita', 'Premium', 'Administrador'];
const empty = { name: '', slug: '', launchUrl: '', categorySlug: '', description: '', iconUrl: '', isPro: false, price: 0, currency: 'MXN', accessDays: null as number | null, featuredToday: false, addToMenu: false };
const date = (s: string | null) => (s ? new Date(s).toLocaleDateString() : 'permanente');

export function Admin() {
  const [tab, setTab] = useState<'games' | 'users'>('games');
  const [msg, setMsg] = useState('');
  return (
    <section><h2>Administración</h2>
      <div className="chips">
        <button className={tab === 'games' ? 'on' : ''} onClick={() => setTab('games')}>Juegos y precios</button>
        <button className={tab === 'users' ? 'on' : ''} onClick={() => setTab('users')}>Usuarios</button>
      </div>
      {msg && <p role="status">{msg}</p>}
      {tab === 'games' ? <GamesTab say={setMsg} /> : <UsersTab say={setMsg} />}
    </section>
  );
}

type Say = (m: string) => void;
const useRun = (say: Say) => async (fn: () => Promise<unknown>, ok: string, after?: () => Promise<unknown> | void) => {
  say('');
  try { await fn(); say(ok); await after?.(); } catch (e) { say((e as Error).message); }
};

function GameRow({ g, onSave, onEdit, onToggle }: { g: AdminGame; onSave: (g: AdminGame) => void; onEdit: (g: AdminGame) => void; onToggle: (g: AdminGame) => void }) {
  const [d, setD] = useState(g);
  useEffect(() => setD(g), [g]);
  const dirty = d.isPro !== g.isPro || d.price !== g.price || d.accessDays !== g.accessDays;
  return (
    <article className="card admin-row">
      <div className="grow"><h3>{g.iconUrl.startsWith('emoji:') ? g.iconUrl.slice(6) : ''} {g.name} {!g.isActive && '(inactivo)'}</h3>
        <small>{g.categorySlug} · {g.slug}</small></div>
      <label><input type="checkbox" checked={d.isPro} onChange={e => setD({ ...d, isPro: e.target.checked, price: e.target.checked ? d.price : 0 })} /> Pro</label>
      <input type="number" min={0} step="0.01" aria-label="Precio" title="Precio (0 = solo suscripción)" disabled={!d.isPro} value={d.price} onChange={e => setD({ ...d, price: Number(e.target.value) })} />
      <input type="number" min={1} aria-label="Días de acceso" title="Días de acceso (vacío = permanente)" placeholder="días" disabled={!d.isPro} value={d.accessDays ?? ''} onChange={e => setD({ ...d, accessDays: e.target.value ? Number(e.target.value) : null })} />
      <button disabled={!dirty} onClick={() => onSave(d)}>Guardar</button>
      <button className="link" onClick={() => onEdit(g)}>Editar</button>
      <button className="link" onClick={() => onToggle(g)}>{g.isActive ? 'Desactivar' : 'Activar'}</button>
    </article>
  );
}

function GamesTab({ say }: { say: Say }) {
  const run = useRun(say);
  const [games, setGames] = useState<AdminGame[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  const [q, setQ] = useState('');
  const [cat, setCat] = useState('');
  const [kind, setKind] = useState<'' | 'pro' | 'demo'>('');
  const [f, setF] = useState(empty);
  const [grant, setGrant] = useState({ email: '', slug: '', days: '' });
  const load = () => api.adminGames().then(setGames).catch(e => say((e as Error).message));
  useEffect(() => { load(); api.categories().then(c => { setCats(c); setF(x => ({ ...x, categorySlug: c[0]?.slug ?? '' })); }); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const shown = useMemo(() => games.filter(g =>
    (!q || (g.name + g.slug).toLowerCase().includes(q.toLowerCase())) && (!cat || g.categorySlug === cat) && (!kind || (kind === 'pro') === g.isPro)), [games, q, cat, kind]);
  const full = (g: AdminGame | typeof empty) => ({ ...g, price: g.isPro ? g.price : 0, addToMenu: 'addToMenu' in g ? g.addToMenu : false });
  const save = (g: AdminGame | typeof empty, ok: string) => run(() => api.adminSave(full(g) as never), ok, load);

  return (
    <>
      <div className="filters">
        <input type="search" placeholder="Buscar juego…" value={q} onChange={e => setQ(e.target.value)} />
        <select value={cat} onChange={e => setCat(e.target.value)} aria-label="Categoría"><option value="">Todas las categorías</option>{cats.map(c => <option key={c.id} value={c.slug}>{c.name}</option>)}</select>
        <select value={kind} onChange={e => setKind(e.target.value as '' | 'pro' | 'demo')} aria-label="Tipo"><option value="">Demo y Pro</option><option value="demo">Solo demo</option><option value="pro">Solo Pro</option></select>
      </div>
      <p><small>{shown.length} de {games.length} juegos. Pro con precio 0 = solo suscripción; sin días = acceso permanente.</small></p>
      {shown.map(g => <GameRow key={g.id} g={g} onSave={x => save(x, `${x.name} guardado.`)} onEdit={x => { setF({ ...x, addToMenu: false }); window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' }); }}
        onToggle={x => run(() => api.adminActive(x.slug, !x.isActive), 'Estado actualizado.', load)} />)}

      <form className="admin" onSubmit={(e: FormEvent) => { e.preventDefault(); save(f, 'Juego guardado.'); }}>
        <h3>Agregar / editar juego</h3>
        <input placeholder="Nombre" required value={f.name} onChange={e => setF({ ...f, name: e.target.value })} />
        <input placeholder="slug (mi-juego)" required pattern="[a-z0-9]+(-[a-z0-9]+)*" value={f.slug} onChange={e => setF({ ...f, slug: e.target.value })} />
        <input placeholder="URL del juego (https://…)" type="url" required value={f.launchUrl} onChange={e => setF({ ...f, launchUrl: e.target.value })} />
        <input placeholder="Icono: URL de imagen o emoji:🎲" value={f.iconUrl} onChange={e => setF({ ...f, iconUrl: e.target.value })} />
        <input placeholder="Descripción" value={f.description} onChange={e => setF({ ...f, description: e.target.value })} />
        <select value={f.categorySlug} onChange={e => setF({ ...f, categorySlug: e.target.value })}>{cats.map(c => <option key={c.id} value={c.slug}>{c.name}</option>)}</select>
        <label><input type="checkbox" checked={f.isPro} onChange={e => setF({ ...f, isPro: e.target.checked })} /> Pro (si no, es demo abierto)</label>
        {f.isPro && <>
          <input type="number" min={0} step="0.01" placeholder="Precio (0 = solo suscripción)" value={f.price} onChange={e => setF({ ...f, price: Number(e.target.value) })} />
          <input placeholder="Moneda" maxLength={3} value={f.currency} onChange={e => setF({ ...f, currency: e.target.value.toUpperCase() })} />
          <input type="number" min={1} placeholder="Días de acceso (vacío = permanente)" value={f.accessDays ?? ''} onChange={e => setF({ ...f, accessDays: e.target.value ? Number(e.target.value) : null })} />
        </>}
        <label><input type="checkbox" checked={f.featuredToday} onChange={e => setF({ ...f, featuredToday: e.target.checked })} /> Recomendado del día</label>
        <label><input type="checkbox" checked={f.addToMenu} onChange={e => setF({ ...f, addToMenu: e.target.checked })} /> Agregar al submenú de Categorías</label>
        <div><button>Guardar</button> <button type="button" className="link" onClick={() => setF({ ...empty, categorySlug: cats[0]?.slug ?? '' })}>Limpiar</button></div>
      </form>

      <form className="admin" onSubmit={e => { e.preventDefault(); run(() => api.adminGrant(grant.email, grant.slug, grant.days ? Number(grant.days) : null), 'Permiso otorgado.'); }}>
        <h3>Dar permiso de un juego Pro a un usuario</h3>
        <input type="email" placeholder="Email del usuario" required value={grant.email} onChange={e => setGrant({ ...grant, email: e.target.value })} />
        <select required value={grant.slug} onChange={e => setGrant({ ...grant, slug: e.target.value })}>
          <option value="">Juego Pro…</option>{games.filter(g => g.isPro).map(g => <option key={g.id} value={g.slug}>{g.name}</option>)}
        </select>
        <input type="number" min={1} placeholder="Días (vacío = permanente)" value={grant.days} onChange={e => setGrant({ ...grant, days: e.target.value })} />
        <button>Otorgar</button>
      </form>
    </>
  );
}

function UsersTab({ say }: { say: Say }) {
  const run = useRun(say);
  const { user: me } = useAuth();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [q, setQ] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const [perms, setPerms] = useState<AdminUserGame[]>([]);
  const load = () => api.adminUsers(q.trim() || undefined).then(setUsers).catch(e => say((e as Error).message));
  useEffect(() => { const t = setTimeout(load, 250); return () => clearTimeout(t); }, [q]); // eslint-disable-line react-hooks/exhaustive-deps
  const toggle = async (id: string) => {
    if (open === id) { setOpen(null); return; }
    setOpen(id); setPerms(await api.adminUserGames(id).catch(() => []));
  };

  return (
    <>
      <input type="search" className="search" placeholder="Buscar por nombre o email…" value={q} onChange={e => setQ(e.target.value)} />
      <p><small>{users.length} usuarios (máx. 200). Cambiar el tipo de cuenta cierra las sesiones de ese usuario.</small></p>
      {users.map(u => (
        <article className="card user" key={u.id}>
          <div className="grow"><h3>{u.name}</h3><small>{u.email} · alta {new Date(u.createdAt).toLocaleDateString()}</small>
            <p><small>Membresía: {u.membershipExpiresAt && new Date(u.membershipExpiresAt) > new Date() ? `${u.membershipPlan} hasta ${date(u.membershipExpiresAt)}` : 'sin membresía'} · Sesiones activas: {u.activeSessions}/2 · Juegos con permiso: {u.grantedGames}</small></p>
            {open === u.id && <ul>{perms.map(p => (
              <li key={p.gameId}>{p.name} — hasta {date(p.expiresAt)} <button className="link" onClick={() => run(() => api.adminRevokeGame(u.id, p.gameId), 'Permiso revocado.', async () => { setPerms(await api.adminUserGames(u.id)); await load(); })}>Revocar</button></li>))}
              {!perms.length && <li>Sin permisos por juego.</li>}</ul>}
          </div>
          <select aria-label="Tipo de cuenta" value={u.accountType} disabled={u.id === me?.id}
            onChange={e => run(() => api.adminAccountType(u.id, Number(e.target.value) as AccountType), 'Tipo de cuenta actualizado.', load)}>
            {ACCOUNT.map((n, i) => <option key={n} value={i}>{n}</option>)}
          </select>
          <button className="link" onClick={() => { const d = Number(window.prompt('¿Cuántos días de membresía Premium (sin cobro)?', '30')); if (d > 0) run(() => api.adminMembership(u.id, d), 'Membresía otorgada.', load); }}>Dar membresía</button>
          <button className="link" onClick={() => toggle(u.id)}>{open === u.id ? 'Ocultar permisos' : 'Ver permisos'}</button>
          <button className="link" disabled={!u.activeSessions} onClick={() => run(() => api.adminRevokeSessions(u.id), 'Sesiones cerradas.', load)}>Cerrar sesiones</button>
        </article>
      ))}
    </>
  );
}
