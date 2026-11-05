import { useEffect, useState, type FormEvent } from 'react';
import { api, type AdminGame, type Category } from '../api';

const empty = { name: '', slug: '', launchUrl: '', categorySlug: '', description: '', iconUrl: '', isPro: false, price: 0, currency: 'MXN', accessDays: null as number | null, featuredToday: false, addToMenu: true };

export function Admin() {
  const [games, setGames] = useState<AdminGame[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  const [f, setF] = useState(empty);
  const [grant, setGrant] = useState({ email: '', slug: '', days: '' });
  const [msg, setMsg] = useState('');
  const load = () => api.adminGames().then(setGames).catch(e => setMsg((e as Error).message));
  useEffect(() => { load(); api.categories().then(c => { setCats(c); setF(x => ({ ...x, categorySlug: c[0]?.slug ?? '' })); }); }, []);

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setMsg('');
    try { await fn(); setMsg(ok); await load(); } catch (e) { setMsg((e as Error).message); }
  };
  const save = (e: FormEvent) => { e.preventDefault(); run(() => api.adminSave({ ...f, price: f.isPro ? f.price : 0 }), 'Juego guardado.'); };
  const edit = (g: AdminGame) => setF({ ...g, addToMenu: false });

  return (
    <section><h2>Administración</h2>
      {msg && <p role="status">{msg}</p>}
      <form className="admin" onSubmit={save}>
        <h3>Agregar / editar juego</h3>
        <input placeholder="Nombre" required value={f.name} onChange={e => setF({ ...f, name: e.target.value })} />
        <input placeholder="slug (mi-juego)" required pattern="[a-z0-9]+(-[a-z0-9]+)*" value={f.slug} onChange={e => setF({ ...f, slug: e.target.value })} />
        <input placeholder="URL del juego (https://…)" type="url" required value={f.launchUrl} onChange={e => setF({ ...f, launchUrl: e.target.value })} />
        <input placeholder="URL del icono" value={f.iconUrl} onChange={e => setF({ ...f, iconUrl: e.target.value })} />
        <input placeholder="Descripción" value={f.description} onChange={e => setF({ ...f, description: e.target.value })} />
        <select value={f.categorySlug} onChange={e => setF({ ...f, categorySlug: e.target.value })}>{cats.map(c => <option key={c.id} value={c.slug}>{c.name}</option>)}</select>
        <label><input type="checkbox" checked={f.isPro} onChange={e => setF({ ...f, isPro: e.target.checked })} /> Pro (si no, es demo abierto)</label>
        {f.isPro && <>
          <input type="number" min={0} step="0.01" placeholder="Precio (0 = solo suscripción)" value={f.price} onChange={e => setF({ ...f, price: Number(e.target.value) })} />
          <input placeholder="Moneda" maxLength={3} value={f.currency} onChange={e => setF({ ...f, currency: e.target.value.toUpperCase() })} />
          <input type="number" min={1} placeholder="Días de acceso (vacío = permanente)" value={f.accessDays ?? ''} onChange={e => setF({ ...f, accessDays: e.target.value ? Number(e.target.value) : null })} />
        </>}
        <label><input type="checkbox" checked={f.featuredToday} onChange={e => setF({ ...f, featuredToday: e.target.checked })} /> Recomendado del día</label>
        <label><input type="checkbox" checked={f.addToMenu} onChange={e => setF({ ...f, addToMenu: e.target.checked })} /> Agregar al menú</label>
        <button>Guardar</button> <button type="button" className="link" onClick={() => setF({ ...empty, categorySlug: cats[0]?.slug ?? '' })}>Limpiar</button>
      </form>

      <h3>Juegos</h3>
      {games.map(g => (
        <article className="card" key={g.id}>
          <div className="grow"><h3>{g.name} {!g.isActive && '(inactivo)'}</h3>
            <small>{g.slug} · {g.isPro ? `Pro · ${g.price} ${g.currency} · ${g.accessDays ?? 'permanente'}` : 'Demo'}</small></div>
          <button onClick={() => edit(g)}>Editar</button>
          <button className="link" onClick={() => run(() => api.adminActive(g.slug, !g.isActive), 'Estado actualizado.')}>{g.isActive ? 'Desactivar' : 'Activar'}</button>
        </article>
      ))}

      <form className="admin" onSubmit={e => { e.preventDefault(); run(() => api.adminGrant(grant.email, grant.slug, grant.days ? Number(grant.days) : null), 'Permiso otorgado.'); }}>
        <h3>Dar permiso de un juego a un usuario</h3>
        <input type="email" placeholder="Email del usuario" required value={grant.email} onChange={e => setGrant({ ...grant, email: e.target.value })} />
        <select required value={grant.slug} onChange={e => setGrant({ ...grant, slug: e.target.value })}>
          <option value="">Juego…</option>{games.filter(g => g.isPro).map(g => <option key={g.id} value={g.slug}>{g.name}</option>)}
        </select>
        <input type="number" min={1} placeholder="Días (vacío = permanente)" value={grant.days} onChange={e => setGrant({ ...grant, days: e.target.value })} />
        <button>Otorgar</button>
      </form>
    </section>
  );
}
