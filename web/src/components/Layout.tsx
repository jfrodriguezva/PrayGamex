import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { api, type MenuItem } from '../api';
import { useAuth } from '../auth';
import { useDevice } from '../useDevice';

const ICONS: Record<string, string> = { home: '??', grid: '???', star: '?', user: '??' };

/** El menú viene de BD (api/menu?device=) y el layout cambia según el dispositivo. */
export function Layout() {
  const device = useDevice();
  const { user, logout } = useAuth();
  const [menu, setMenu] = useState<MenuItem[]>([]);
  useEffect(() => { api.menu(device).then(setMenu).catch(() => setMenu([])); }, [device]);
  return (
    <div className={`layout ${device}`}>
      <nav aria-label="Principal">
        {device !== 'mobile' && <div className="brand">{device === 'desktop' ? 'PrayGamex' : 'PG'}</div>}
        {menu.map(m => (
          <div key={m.key} className="navgroup">
            <NavLink to={m.route} end={m.route === '/'} title={m.label}>
              <span aria-hidden>{ICONS[m.icon] ?? '•'}</span>
              {device !== 'tablet' && <span className="label">{m.label}</span>}
            </NavLink>
            {device === 'desktop' && m.children.length > 0 && (
              <ul>{m.children.map(c => <li key={c.key}><NavLink to={c.route}>{c.label}</NavLink></li>)}</ul>
            )}
          </div>
        ))}
        {device === 'desktop' && <button className="link" onClick={logout}>Salir ({user?.name})</button>}
      </nav>
      <main><Outlet /></main>
    </div>
  );
}
