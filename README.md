# PrayGamex

Capa de **seguridad, pagos y visibilidad** para los juegos de Arcade y Uapeani. Los juegos viven en otros servidores; aquí solo se guarda
su metadato (nombre, icono, categoría, **URL**, **precio**) y se controla quién puede jugarlos.

## Stack
.NET 10 (Clean Architecture) · **Dapper** + SQL Server · Polly (Microsoft.Extensions.Http.Resilience) · React + Vite + TypeScript · Docker

```
src/
  PrayGamex.Domain          entidades y enums
  PrayGamex.Application     casos de uso + interfaces de repositorio
  PrayGamex.Infrastructure  repositorios Dapper, esquema SQL, JWT, PBKDF2, Mercado Pago (+Polly)
  PrayGamex.Api             controladores, JWT bearer, rate limiting, CORS
web/                        SPA React (menú distinto para móvil, tablet y PC)
```

## Reglas de negocio
- **Demo vs Pro** (`Games.IsPro`): los **demo** son abiertos para cualquier usuario autenticado. Los **pro** requieren
  **membresía vigente** o un **permiso por juego** (`UserGames`), que se obtiene por **compra** o por asignación manual.
- **Precio por juego en BD** (`Games.Price`, `Currency`, `AccessDays`): `Price > 0` habilita la compra individual; `AccessDays` NULL = permanente.
  El cliente nunca envía montos: el cobro lee el precio de BD y el webhook verifica que lo pagado cubra ese monto.
- **Máximo 2 sesiones por usuario**: al iniciar sesión por tercera vez se cierra la sesión **más antigua**; su access token deja de
  funcionar en el siguiente request y su refresh token se revoca. Un refresh token reutilizado cierra su sesión.
- **Membresías** (`MembershipPlans`, precios en BD): dan acceso a todos los juegos Pro mientras estén vigentes.

## Catálogo inicial (sembrado al crear la BD)
- **99 juegos de Arcade** (demo, abiertos) en 5 categorías: Mesa, Tradición, Arcade, Mente y Niños. URL de prueba:
  `https://jfrodriguezva.github.io/Arcade/demo/?juego=<nombre>` (el Arcade ya entiende `?juego=`).
- **3 de Uapeani** (Mictlania, Uapeania, UapeaniBot), Pro y solo por suscripción hasta que les pongas precio:
  `https://jfrodriguezva.github.io/Uapeani/demo/?juego=<nombre>`.
- Origen: `src/PrayGamex.Infrastructure/Persistence/catalogo.json`. Cuando exista el servidor de juegos, cambia las URLs desde `/admin`
  (o `Games:ArcadeBaseUrl` / `Games:UapeaniBaseUrl` antes de crear la BD). Los juegos sembrados no se agregan al submenú lateral (son 100+).

## Alta de un juego desde BD (precio + URL + menú)
```sql
-- Demo (abierto)
EXEC dbo.usp_AddGame @Name='Mi demo', @Slug='mi-demo', @LaunchUrl='https://servidor-juegos/mi-demo', @CategorySlug='arcade', @IsPro=0;
-- Pro: se compra por 79 MXN con acceso permanente (@AccessDays=30 → 30 días); @Price=0 → solo suscripción
EXEC dbo.usp_AddGame @Name='Mi pro', @Slug='mi-pro', @LaunchUrl='https://servidor-juegos/mi-pro', @CategorySlug='arcade',
     @IsPro=1, @Price=79, @Currency='MXN', @AccessDays=NULL, @IconUrl='/icons/mi-pro.svg';
-- Dar permiso manual a un usuario (NULL = permanente)
EXEC dbo.usp_GrantGame @Email='usuario@correo.com', @Slug='mi-pro', @Days=NULL;
```
`usp_AddGame` inserta o actualiza el juego (así se cambia su precio/URL) y lo agrega al submenú de *Categorías* en móvil, tablet y PC.

## Administración
- Hacer admin a un usuario: `EXEC dbo.usp_SetAccountType @Email='yo@correo.com', @AccountType=2;` (vuelve a iniciar sesión). En la web aparece
  el enlace **Administración** en *Perfil*: alta/edición de juegos (URL, demo/pro, precio, días), activar/desactivar y permisos por usuario.
- **Juegos y precios:** cada fila trae Pro sí/no, precio y días de acceso con su botón Guardar; hay búsqueda y filtros por categoría y tipo.
- **Usuarios:** lista con búsqueda, tipo de cuenta (cambiarlo cierra sus sesiones), membresía, sesiones activas y permisos por juego (ver y revocar),
  **Dar membresía** manual por N días y **Cerrar sesiones**. No puedes cambiar tu propio tipo de cuenta.
- API: `GET/PUT /api/admin/games`, `GET /api/admin/users?q=`, `POST /api/admin/users/{id}/account-type|membership|revoke-sessions`, `GET /api/admin/users/{id}/games`, `DELETE /api/admin/users/{id}/games/{gameId}`,
  `GET/PUT /api/admin/games`, `POST /api/admin/games/{slug}/active`, `POST /api/admin/grants` (rol Admin).

## Cómo un juego (servidor externo) valida al jugador
`POST /api/games/{id}/launch` valida el permiso y devuelve la URL del juego con `?pgt=<token>` (JWT de 4 h, audiencia propia, solo válido para ese juego).
El servidor del juego puede: (1) `POST /api/launch/verify {token}` → `{valid,userId,gameId}` (comprueba firma, caducidad y que la sesión siga abierta), y
(2) guardar el avance con `PUT /api/games/{id}/progress` usando ese token como `Authorization: Bearer`. El token no sirve en ningún otro endpoint ni para otro juego.

## Webhook de Mercado Pago
Configura `MercadoPago:WebhookSecret` (clave secreta de Webhooks en el panel de MP). Se verifica `x-signature`; sin secreto solo se aceptan webhooks en Development.

## API (resumen)
| | |
|---|---|
| `POST /api/auth/register · login · refresh · logout` | autenticación (rate limit) |
| `GET /api/menu?device=mobile\|tablet\|desktop` | menú desde BD |
| `GET /api/games?q=&categoryId=&favorites=` · `/featured` · `/recents` · `/challenges` · `/categories` | catálogo (incluye `isPro`, `price`, `locked`) |
| `POST /api/games/{id}/favorite` · `/launch` | favorito (estrella) · valida permiso y devuelve la URL |
| `GET/PUT /api/games/{id}/progress` | avance por usuario y juego (JSON, máx. 256 KB) |
| `GET /api/payments/plans` · `POST /api/payments/checkout` `{gameId}` o `{planCode}` | cobro con Mercado Pago |
| `POST /api/payments/webhook` | aviso de Mercado Pago (se re-consulta el pago a su API; idempotente) |

## Ejecutar con Docker
```
cp .env.example .env     # edita contraseñas y JWT_KEY
docker compose up --build
```
Web: http://localhost:8081 · API: http://localhost:5080 (`/health`).
La BD, tablas y procedimientos se crean solos al arrancar (idempotente, con reintentos Polly mientras SQL Server levanta).

## Desarrollo local
```
docker compose up db -d
dotnet run --project src/PrayGamex.Api
cd web && npm install && npm run dev        # http://localhost:5173 (proxy a :5080)
```

## Pruebas
`dotnet test tests/PrayGamex.Tests` (28 pruebas: seguridad, sesiones, acceso demo/pro, webhook de pago, validación de admin). CI en `.github/workflows/ci.yml`.

## Pendiente (depende de ti)
- Registrar los juegos reales de Arcade/Uapeani desde el panel o con `usp_AddGame` (hay 2 de ejemplo sembrados).
- Credenciales de Mercado Pago (`MP_ACCESS_TOKEN`, `MP_WEBHOOK_SECRET`) y la URL pública del webhook.
- Cambiar `Jwt:Key` y la contraseña de SQL Server en producción.
