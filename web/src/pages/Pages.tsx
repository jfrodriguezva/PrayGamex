import { useEffect, useState, type FormEvent } from 'react';
import { api, type Category, type Challenge, type Game, type Plan } from '../api';
import { useAuth } from '../auth';
import { GameCard } from '../components/GameCard';

export function Login() {
  const { login, register } = useAuth();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [f, setF] = useState({ email: '', name: '', password: '' });
  const [err, setErr] = useState('');
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr('');
    try { mode === 'login' ? await login(f.email, f.password) : await register(f.email, f.name, f.password); }
    catch (x) { setErr((x as Error).message); }
  };
  return (
    <form className="auth" onSubmit={submit}>
      <h1>PrayGamex</h1>
      <input type="email" placeholder="Email" required value={f.email} onChange={e => setF({ ...f, email: e.target.value })} />
      {mode === 'register' && <input placeholder="Nombre" required value={f.name} onChange={e => setF({ ...f, name: e.target.value })} />}
      <input type="password" placeholder="Contraseña (mín. 8)" minLength={8} required value={f.password} onChange={e => setF({ ...f, password: e.target.value })} />
      {err && <p className="error">{err}</p>}
      <button>{mode === 'login' ? 'Entrar' : 'Crear cuenta'}</button>
      <button type="button" className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
        {mode === 'login' ? 'Crear una cuenta' : 'Ya tengo cuenta'}
      </button>
    </form>
  );
}

export function Home() {
  const [q, setQ] = useState('');
  const [games, setGames] = useState<Game[]>([]);
  const [challenges, setChallenges] = useState<Challenge[]>([]);
  const [recents, setRecents] = useState<Game[]>([]);
  useEffect(() => { api.challenges().then(setChallenges); api.recents().then(setRecents); }, []);
  useEffect(() => { const t = setTimeout(() => { if (q.trim()) api.games(q).then(setGames); else setGames([]); }, 250); return () => clearTimeout(t); }, [q]);
  return (
    <>
      <input type="search" className="search" placeholder="Buscar juegos…" value={q} onChange={e => setQ(e.target.value)} />
      {q.trim() && <section><h2>Resultados</h2>{games.map(g => <GameCard key={g.id} game={g} />)}{!games.length && <p>Sin resultados.</p>}</section>}
      <section id="retos"><h2>Retos</h2>
        {challenges.map(c => <article className="card" key={c.id}><div className="grow"><h3>{c.title}</h3><small>{c.gameName} · {c.points} pts</small><p>{c.detail}</p></div></article>)}
      </section>
      <section id="recientes"><h2>Recientes</h2>
        {recents.map(g => <GameCard key={g.id} game={g} />)}{!recents.length && <p>Aún no has jugado nada.</p>}
      </section>
    </>
  );
}

export function Categories() {
  const [cats, setCats] = useState<Category[]>([]);
  const [sel, setSel] = useState<string>();
  const [games, setGames] = useState<Game[]>([]);
  const [featured, setFeatured] = useState<Game[]>([]);
  const [challenges, setChallenges] = useState<Challenge[]>([]);
  useEffect(() => { api.categories().then(setCats); api.featured().then(setFeatured); }, []);
  useEffect(() => { api.games(undefined, sel).then(setGames); }, [sel]);
  useEffect(() => { api.challenges().then(setChallenges); }, []);
  return (
    <>
      <section><h2>Recomendados del día</h2>{featured.map(g => <GameCard key={g.id} game={g} />)}</section>
      <section><h2>Categorías</h2>
        <div className="chips">
          <button className={!sel ? 'on' : ''} onClick={() => setSel(undefined)}>Todas</button>
          {cats.map(c => <button key={c.id} className={sel === c.id ? 'on' : ''} onClick={() => setSel(c.id)}>{c.name}</button>)}
        </div>
        {games.map(g => (
          <div key={g.id}>
            <GameCard game={g} />
            {challenges.filter(c => c.gameId === g.id).map(c => <p key={c.id} className="challenge">?? {c.title} — {c.points} pts</p>)}
          </div>
        ))}
      </section>
    </>
  );
}

export function Favorites() {
  const [games, setGames] = useState<Game[]>([]);
  const [cats, setCats] = useState<Category[]>([]);
  const [cat, setCat] = useState('');
  const load = () => api.games(undefined, cat || undefined, true).then(setGames);
  useEffect(() => { api.categories().then(setCats); }, []);
  useEffect(() => { load(); }, [cat]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <section><h2>Favoritos</h2>
      <select value={cat} onChange={e => setCat(e.target.value)} aria-label="Filtrar por categoría">
        <option value="">Todas las categorías</option>{cats.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
      </select>
      {games.map(g => <GameCard key={g.id} game={g} onChange={load} />)}{!games.length && <p>No tienes favoritos con este filtro.</p>}
    </section>
  );
}

const ACCOUNT = ['Gratuita', 'Premium', 'Administrador'];
export function Profile() {
  const { user } = useAuth();
  const [err, setErr] = useState('');
  const [plans, setPlans] = useState<Plan[]>([]);
  useEffect(() => { api.plans().then(setPlans); }, []);
  if (!user) return null;
  const buy = async (code: string) => { try { window.location.href = (await api.buyPlan(code)).checkoutUrl; } catch (e) { setErr((e as Error).message); } };
  const m = user.membership;
  return (
    <section id="membresia"><h2>Perfil</h2>
      <dl><dt>Nombre</dt><dd>{user.name}</dd><dt>Email</dt><dd>{user.email}</dd><dt>Tipo de cuenta</dt><dd>{ACCOUNT[user.accountType]}</dd>
        <dt>Membresía</dt><dd>{m?.isActive ? `${m.plan} · vence ${new Date(m.expiresAt).toLocaleDateString()}` : 'Sin membresía activa'}</dd></dl>
      {plans.map(p => <button key={p.code} onClick={() => buy(p.code)}>{p.name} · {p.price} {p.currency} · {p.days} días (Mercado Pago)</button>)}
      {err && <p className="error">{err}</p>}
      {user.accountType === 2 && <p><a href="/admin">Administración de juegos y permisos</a></p>}
    </section>
  );
}
