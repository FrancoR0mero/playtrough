# Frontend

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.2.2.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test --watch=false
```

Authentication tests use the existing Vitest runner, Angular TestBed and
HttpTestingController. The full suite, including catalog search, has 469 passing cases.
Service tests cover login/session restoration and logout, including the bearer
header, server/network failures, missing refresh tokens and the five-second timeout.
Storage tests cover persistence, cleanup and corrupt or expired data.

Login and header tests use mocked AuthService and TokenStorageService dependencies;
Router navigation is spied while its URL parsing remains real. Login tests live in
`login.spec.ts`; existing registration cases remain in `register.spec.ts`.
HTTP tests verify there are no pending requests, and authentication storage is
cleared between tests. No backend or browser E2E server is needed.

AuthService has no public `clearSession()` method. Cleanup is verified through
`logout()`, refresh failures and TokenStorageService's `clear()`.

## Session refresh

`AuthService.refresh()` calls the public refresh endpoint without Authorization and
returns an Observable of the new access token. It replaces both stored tokens and
the user, including the rotated refresh token. Concurrent subscribers share one
request; a completed/failed request is released so a later call can start another.
Stale refresh responses cannot restore a logged-out session or overwrite a newer login.

HTTP 400/401 and local persistence failures clear the session and redirect to
login. A 401 adds `sessionExpired=true`, which shows an accessible notice dismissed
on form submission or switching to registration. Network failures (HTTP status 0)
and server failures (5xx), as well as rate limiting (429), are propagated without
clearing credentials, allowing an explicit later retry.

Expired access tokens do not authenticate a user, but stored refresh credentials
are retained for renewal. `getAccessToken()` returns the current stored access token;
`isAccessTokenExpired(leewaySeconds = 30)` checks expiresAt with a configurable
warning window (0 checks exact expiry). Refresh is explicit: no interceptor,
automatic retry, guard changes or scheduled refresh is included.

`auth-refresh.spec.ts` covers HTTP contracts, token replacement, error handling,
concurrency, terminal callback retries, expiry and stale response protection.

## Respuestas 429 y cuenta regresiva

El interceptor transforma los 429 de la API en `RateLimitError` (subclase de
`HttpErrorResponse`) con `retryAfterSeconds`. Se conservan estado, body, headers y
URL. `Retry-After` se interpreta como un entero positivo de segundos; ausente,
vacío, cero, negativo, fraccionario, fuera del rango entero seguro o con otro formato
usa 60 segundos. No se usan mensajes del servidor para construir el aviso.

El 429 se propaga sin iniciar refresh, expirar la sesión ni redirigir. Si un refresh
iniciado por un 401 recibe un 429, se conservan las credenciales y no se vuelve a
intentar automáticamente. Lo mismo ocurre si el reintento posterior a un refresh
exitoso recibe 429. Login y registro preservan este error tipado en lugar de
convertirlo en un error genérico del formulario.

`rateLimitMessage` genera el mismo texto para toda la app:
`Demasiados intentos. Probá de nuevo en X segundos`. Para más de 60 segundos usa
minutos y segundos, por ejemplo `1 minuto y 30 segundos`. Los avisos tienen
`role="alert"`.

Login, registro, olvidé mi contraseña, reenvío de confirmación, restablecimiento y
confirmación de email usan `createRateLimitCooldown`, que reutiliza
`createResendCooldown` con la duración recibida. El mensaje y el botón muestran el
tiempo restante; el botón se deshabilita y el handler ignora nuevos envíos hasta
llegar a cero. Después se habilita si el formulario sigue siendo válido. No se
reintenta automáticamente ni se guardan límites en storage. Login y registro
mantienen cuentas independientes aunque compartan componente. Los tiempos de
reenvío tras un éxito (30/60 segundos) se conservan. Los timers se eliminan al
destruir su componente.

En confirmación de email, un 429 permite reintentar con el mismo token mantenido
solo en memoria; no se considera un enlace inválido. En roles, tanto la carga de
usuarios como el cambio de rol muestran el aviso sin alterar la sesión ni agregar
una cuenta regresiva. Un guard que recibe 429 al renovar cancela la navegación y
muestra el aviso compartido en el layout, con un botón para cerrarlo.

Los tests usan HttpTestingController con el interceptor y AuthService reales,
componentes renderizados y fake timers. Cubren las seis pantallas con y sin
Retry-After, errores tipados, formatos inválidos, minutos/segundos, bloqueo de
envíos, reactivación, limpieza de timers, refresh y sus reintentos, roles y guards.
Los tests E2E no forman parte de esta implementación.

### Prueba manual en desarrollo

1. Desde la raíz del repositorio, iniciá la API con un límite temporal de tres
   logins por minuto. Los argumentos solo afectan ese arranque, sin editar archivos:

   ```powershell
   dotnet run --project backend/src/Playtrough.Api --launch-profile http -- --RateLimit:Login:PermitLimit=3 --RateLimit:Login:Window=00:01:00
   ```

2. Desde `frontend`, ejecutá `npm start` y abrí
   [http://localhost:4200/auth/login](http://localhost:4200/auth/login).
   Enviá cuatro intentos con una cuenta inexistente, por ejemplo
   `rate-limit-check@example.invalid`, para evitar bloquear una cuenta real. Con
   presupuesto inicial disponible, los primeros tres responden 401 y el cuarto 429.
3. Verificá el mensaje, el botón deshabilitado y la cuenta regresiva. En Network no
   deben aparecer llamadas a refresh ni logout a raíz del 429. Al terminar la
   espera, el botón vuelve a habilitarse y permite un nuevo intento explícito.
4. Para roles, agregá `--RateLimit:Global:PermitLimit=3` al arranque anterior y usá
   una cuenta de prueba Admin. Cambiá filtros o recargá la lista hasta recibir 429:
   se muestra el mismo aviso y la sesión y la ruta permanecen sin cambios.

**Retry-After y CORS:** con frontend en 4200 y API en 5000 son orígenes distintos.
La configuración actual del backend no expone `Retry-After` mediante
`Access-Control-Expose-Headers`. Aunque Network muestre el header, el navegador
puede impedir leerlo desde JavaScript: en ese caso se aplican los 60 segundos
predeterminados. Si el header es accesible (por ejemplo, con un despliegue del
mismo origen), se utiliza su valor. Esta tarea no cambia la configuración CORS.

## Catálogo de juegos — fase 1 (US-20)

`/games` requiere sesión y aparece como **Juegos** para User, Moderator y Admin.
`GameCatalogService` consume búsqueda y detalle desde `environment.apiUrl`.
El pie del layout conserva la identidad existente e incluye un enlace activo a RAWG
en todas las pantallas. El modal también lo incluye porque puede cubrir el pie.

La búsqueda usa Reactive Forms, un mínimo de dos caracteres después de `trim()`
y 400 ms de espera al escribir. Cada cambio cancela inmediatamente la petición
anterior, incluso antes de terminar esa espera. `q` se sincroniza con la URL
usando `replaceUrl`, sin crear una entrada de historial por tecla; abrir un enlace
o navegar a otra búsqueda restaura el campo. Las páginas tienen 20 elementos:
**Cargar más** conserva los anteriores, elimina duplicados por `externalId` y
bloquea cargas simultáneas. Si falla una página, se reintenta esa misma página.

Las imágenes horizontales se recortan al centro. `GAME_CARD_ASPECT_RATIO`, en
`src/app/features/games/games.ts`, define la proporción de tarjetas y esqueletos
en un único lugar (`3 / 4`). Una imagen nula o fallida muestra el mismo placeholder.
Los errores leen `extensions.code` y aceptan también `code` en la raíz, como las
pantallas existentes. El 429 reutiliza el interceptor y la cuenta regresiva:
cambiar el texto no evita la espera y al terminar se requiere reintento explícito.

**Límite acordado entre fases:** las tarjetas ya abren una vista previa accesible
con CDK Dialog: título, imagen, año, cierre con Escape/clic fuera, foco atrapado,
restauración a la tarjeta y bloqueo del scroll cuando el fondo puede desplazarse.
En esta fase no se llama al endpoint de detalle desde el modal. La carga del
detalle completo y sus secciones corresponden a US-21 (fase 2).

Los nuevos tests cubren contratos HTTP, debounce, cancelación, URL real y
parámetros de ruta, paginación, errores y reintentos, limpieza de timers,
placeholders, modal, guards, navegación por rol y atribución global. Se ejecutan
con `npm test -- --watch=false`, sin backend ni tests E2E. En jsdom se comprueba
la semántica del botón nativo y su activación mediante `click` de teclado; la
activación real por Enter/Espacio se incluye en la prueba manual.

### Prueba manual con backend

1. Con la configuración habitual de base de datos, JWT y correo disponible,
   arrancá el backend desde la raíz con el proveedor falso:

   ```powershell
   dotnet run --project backend/src/Playtrough.Api --launch-profile http -- --Catalog:UseFake=true
   ```

   Para usar RAWG real, seguí la configuración de proveedor del README del backend,
   con `Catalog:UseFake=false`. La clave permanece en el backend.
2. Desde `frontend`, ejecutá `npm start`, iniciá sesión y abrí **Juegos**.
   Buscá `Portal`, comprobá `?q=Portal` y recargá: se conserva la búsqueda.
   Verificá el enlace a RAWG al pie.
3. Escribí distintos nombres rápidamente; en Network, usando una conexión lenta,
   comprobá que se cancelan búsquedas anteriores y solo aparecen resultados del
   texto actual. Con menos de dos caracteres no debe enviarse una búsqueda.
4. Con RAWG real, buscá un término amplio como `Mario` y usá **Cargar más**:
   debe conservar las tarjetas y bloquear clics dobles mientras carga. El proveedor
   falso tiene solo 20 juegos en total; con páginas de 20 no permite comprobar
   manualmente ese botón. La paginación queda cubierta con respuestas simuladas
   en los tests, sin modificar el backend ni su catálogo.
5. Con Tab, enfocá una tarjeta y abrila con Enter o Espacio. Recorré el modal con
   Tab/Shift+Tab y cerralo con Escape: el foco vuelve a esa tarjeta. Repetí el cierre
   haciendo clic fuera en escritorio y usando **Cerrar** en móvil.
6. El proveedor falso devuelve juegos sin imagen: comprobá los placeholders.
   Con RAWG real también podés bloquear una URL de imagen desde DevTools para
   comprobar el fallback ante errores de carga.
7. Detené el backend y cambiá la búsqueda: debe mostrarse el error de conexión.
   Reinicialo y pulsá **Reintentar**. Para comprobar el 503, arrancá con
   `--Catalog:UseFake=false --RawgSettings:Enabled=false` y volvé a buscar.

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
