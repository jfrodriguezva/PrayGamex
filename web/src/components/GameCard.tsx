import { useState } from 'react';
import { api, type Game } from '../api';

function priceLabel(g: Game) {
  if (!g.isPro) return 'Demo gratuito';
  const buy = g.price > 0 ? ` · ${g.price} ${g.currency} ${g.accessDays ? `por ${g.accessDays} días` : '(permanente)'}` : '';
  return `Pro${buy}`;
}

export function GameCard({ game, onChange }: { game: Game; onChange?: () => void }) {
  const [fav, setFav] = useState(game.isFavorite);
  const [err, setErr] = useState('');
  const toggle = async () => { const r = await api.toggleFavorite(game.id); setFav(r.isFavorite); onChange?.(); };
  const play = async () => {
    setErr('');
    try { const { url } = await api.launch(game.id); window.open(url, '_blank', 'noopener'); }
    catch (e) { setErr((e as Error).message); }
  };
  const buy = async () => {
    setErr('');
    try { window.location.href = (await api.buyGame(game.id)).checkoutUrl; }
    catch (e) { setErr((e as Error).message); }
  };
  return (
    <article className="card">
      {game.iconUrl.startsWith('emoji:')
        ? <span className="emoji" aria-hidden>{game.iconUrl.slice(6)}</span>
        : <img src={game.iconUrl} alt="" width={56} height={56} />}
      <div className="grow">
        <h3>{game.name}</h3>
        <small>{game.categoryName} · {priceLabel(game)}</small>
        {err && <p className="error">{err}</p>}
      </div>
      <button className="icon" aria-label={fav ? 'Quitar de favoritos' : 'Marcar favorito'} onClick={toggle}>{fav ? '★' : '☆'}</button>
      {game.locked
        ? (game.price > 0 ? <button onClick={buy}>Comprar</button> : <button disabled>Solo suscripción</button>)
        : <button onClick={play}>Jugar</button>}
    </article>
  );
}
