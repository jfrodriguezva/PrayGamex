import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth';
import { Layout } from './components/Layout';
import { Categories, Favorites, Home, Login, Profile } from './pages/Pages';
import { Admin } from './pages/Admin';

function Gate() {
  const { user, loading } = useAuth();
  if (loading) return <p className="center">Cargando…</p>;
  if (!user) return <Login />;
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="categorias/*" element={<Categories />} />
        <Route path="favoritos" element={<Favorites />} />
        <Route path="perfil/*" element={<Profile />} />
        {user.accountType === 2 && <Route path="admin" element={<Admin />} />}
        <Route path="*" element={<Navigate to="/" />} />
      </Route>
    </Routes>
  );
}
export default function App() { return <BrowserRouter><AuthProvider><Gate /></AuthProvider></BrowserRouter>; }
