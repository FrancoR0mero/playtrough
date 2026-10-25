# Playtrough backend

ASP.NET Core API (.NET 10) with Identity, Entity Framework Core and MySQL.

Run these commands from the repository root:

```powershell
dotnet build backend/Playtrough.sln --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet run --project backend/src/Playtrough.Api
```

Swagger está disponible en [http://localhost:5000/swagger](http://localhost:5000/swagger)
solo en Development con `Swagger:Enabled=true` (predeterminado de ese entorno).

## Catálogo de juegos — fase 1 (US-19)

La integración se consume mediante `IGameCatalogProvider`; los endpoints públicos
de búsqueda y detalle están disponibles desde la fase 3. La integración del
proveedor no persiste búsquedas. El proveedor real es un cliente tipado de
`IHttpClientFactory`, sin paquetes adicionales de resiliencia.

Sin configuración nueva, `RawgSettings:Enabled=false`: no exige clave y el
proveedor deshabilitado produce 503 ProblemDetails con `code=catalog_unavailable`.
El error tiene el mismo contrato en Development y Production; otras excepciones
conservan el manejo existente. Siguen siendo necesarias las configuraciones
habituales de base de datos, JWT y correo para arrancar la aplicación.

Para usar datos de ejemplo, configurá `Catalog:UseFake=true` en Development.
Devuelve 20 nombres conocidos con fechas, plataformas, géneros y estudios
**ficticios**, sin imágenes; busca por nombre sin distinguir mayúsculas y pagina
los resultados. El arranque falla si se activa fuera de Development. El modo falso
tiene prioridad sobre el proveedor deshabilitado/real; si también se habilita RAWG,
su configuración debe seguir siendo válida.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Catalog__UseFake = "true"
dotnet run --project backend/src/Playtrough.Api
```

Para RAWG real, desactivá el falso y suministrá la clave privada desde user-secrets
(disponible en Development) o `RawgSettings__ApiKey` en el entorno. No la pegues en
appsettings, ejemplos, URLs de diagnóstico ni archivos `.http`. El binding de la
clave ignora appsettings y argumentos de línea de comandos, incluso si contienen
un valor. Los mensajes de validación muestran únicamente claves y reglas.

```powershell
# Sustituí el marcador localmente; nunca versiones la clave.
dotnet user-secrets set "RawgSettings:ApiKey" "TU_CLAVE_PRIVADA" --project backend/src/Playtrough.Api
dotnet user-secrets set "RawgSettings:Enabled" "true" --project backend/src/Playtrough.Api
$env:Catalog__UseFake = "false"
dotnet run --project backend/src/Playtrough.Api
```

Cada intento consume el limitador compartido: se encola y espacia, sin ráfagas de
ventana. Se permiten tres reintentos ante 429 o 5xx, con esperas de 250/500/1000 ms
o el `Retry-After` del proveedor si exige más (segundos o fecha HTTP). Otros estados,
incluidos 401/403 y redirecciones, no se reintentan. El timeout configurable cubre
cada petición y su cuerpo; las fallas de red, timeout o JSON se traducen a la
excepción pública sin adjuntar la excepción original. Un 404 de detalle devuelve
`null`, que `GameService` traduce a juego inexistente desde la fase 2.

El cliente elimina todos sus loggers HTTP automáticos y deshabilita redirecciones.
Los logs propios solo contienen estados y números de reintento, nunca URL,
cabeceras, cuerpo, clave ni excepciones del transporte. No hay una librería de
resiliencia que pueda registrar la URL por separado.

La caché usa consulta recortada y en minúsculas, página y tamaño, o ID de detalle.
Las llamadas idénticas simultáneas comparten una petición incluso entre distintas
instancias del proveedor. Cancelar un consumidor no cancela a los demás; el cierre
de la aplicación cancela el trabajo compartido. No se cachean errores ni detalles
inexistentes. Los valores vencen a los 10 minutos/24 horas por defecto. Con varias
instancias de aplicación harían falta caché y coordinación distribuidas.

Referencia oficial consultada el 24/10/2026: la
[documentación oficial de RAWG](https://api.rawg.io/docs/?trk=public_post-text)
y su [guía de acceso](https://rawg.io/apidocs). Decisiones de esta integración:

- La clave se envía como query `key`; todos los valores se escapan con `QueryHelpers`.
- Se usan `search`, `page`, `page_size` y `exclude_additions=true`. Las adiciones
  incluyen DLC, ediciones GOTY/otras y aplicaciones complementarias. No se usa
  `exclude_parents`, porque excluiría juegos base con adiciones.
- `next` determina `HasMore`; nunca se sigue esa URL. La referencia consultada
  describe `page_size` sin un máximo numérico explícito: **40 es el tope de nuestro
  contrato**, sin atribuirle una garantía oficial que no se pudo confirmar.
- Solo se toma `description_raw`, con máximo 3000 caracteres; nunca `description`
  HTML. Si falta el texto plano, el resumen queda vacío. La descripción y los
  estudios se obtienen en detalle, no en la lista de búsqueda.
- `background_image` se conserva como imagen principal normalmente horizontal.
  No se encontró un contrato oficial de reescritura resize/crop del CDN: `CoverUrl`
  y `CoverUrlLarge` usan la original, o `null` si falta.

Los tests de catálogo usan un `HttpMessageHandler` simulado, reloj controlable,
captura global de logs y sondas HTTP exclusivas del proyecto de pruebas. No hacen
llamadas reales a RAWG. La fixture existente solo se amplía para no heredar la
clave ni el modo falso del equipo de desarrollo.

## Persistencia local de juegos — fase 2 (US-22)

`IGameService.GetOrCreateAsync(provider, externalId)` crea una ficha local solo al
usarla. Por ahora acepta `rawg` (normaliza espacios y mayúsculas), también cuando el
proveedor de desarrollo es falso. La búsqueda sigue siendo una consulta al catálogo
y no guarda resultados. No hay importaciones masivas, biblioteca ni reseñas.

`Game` contiene un ID local `int`, proveedor e ID externo, nombre, slug, resumen,
imagen original nullable, fecha nullable, tres listas y marcas UTC de creación y
actualización. `Games` tiene un índice único `(Provider, ExternalId)`; ninguna
relación nueva con usuarios. Plataformas, géneros y desarrolladoras se almacenan
como columnas MySQL `json`, serializadas desde `List<string>` con un conversor y
un comparador de valores de EF. Esto evita tablas auxiliares y funciones JSON
específicas del proveedor; no se necesitan filtros SQL por esos valores en esta
épica. El comparador detecta cambios dentro de las listas, como recomienda la
[documentación de EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers).
Se conserva **MySql.EntityFrameworkCore de Oracle**, que es el proveedor real del
repositorio; no se incorpora Pomelo.

Una copia con hasta `Catalog:RefreshAfterDays` días se devuelve sin llamar al
proveedor; el valor predeterminado es 30 (rango 1–3650). Si está más vieja, se
intenta refrescar conservando el ID local y `CreatedAt`. Ante
`GameCatalogUnavailableException`, una copia existente se devuelve sin alterar
`UpdatedAt`, para volver a intentar en el siguiente uso. Sin copia, se propaga el
503 `catalog_unavailable`. Si el proveedor devuelve `null`, se lanza
`GameNotFoundException`: 404 `game_not_found`, incluso si hay una copia vieja;
no se borra esa fila. La cancelación del consumidor se propaga.

La concurrencia se resuelve en la base: las peticiones leen y consultan al proveedor
sin mantener una transacción abierta. Si dos intentan insertar la misma identidad,
el índice único decide cuál guarda. La otra descarta su entidad pendiente y relee
la ficha ganadora. Solo se recupera el fallo si realmente existe esa fila; si no,
se propaga la excepción de persistencia. No se analizan mensajes de MySQL/SQLite
ni se depende de un bloqueo en memoria que falle con varias instancias de API.
Repositorio y servicio son scoped; cada petición tiene su propio DbContext.

La migración `20261025065759_AddGames` crea únicamente `Games` y su índice. El
[SQL generado](sql/20261025065759_AddGames.sql) está listo para revisar y **no se
aplicó a la base local**. No hay aplicación automática al iniciar. Para regenerar
el SQL sin credenciales, red ni seed:

```powershell
dotnet build backend/Playtrough.sln -c Release --disable-build-servers -m:1
dotnet ef migrations script 20261018070349_AddRoleChangeLogs 20261025065759_AddGames --project backend/src/Playtrough.Api --startup-project backend/src/Playtrough.Api --configuration Release --no-build --output backend/sql/20261025065759_AddGames.sql -- --offline
```

`AppDbContextFactory` permite a EF trabajar sin ejecutar `Program` ni IdentitySeeder.
Con `--offline` usa una conexión de diseño sin contraseña que **no abre**; se usa
solo para generar migraciones/SQL. Los comandos explícitos de base sin esa opción
usan la configuración normal (incluidos entorno y user-secrets en Development) y
validan la conexión sin exponerla. La fábrica no valida JWT, correo ni RAWG porque
no inicia la aplicación.

Los tests usan el proveedor de catálogo simulado y SQLite aislado. La carrera
usa ocho conexiones/peticiones independientes sobre una base temporal WAL,
comprueba una sola fila y siete colisiones reales recuperadas del índice único.
También cubren refresco, fallback, 404/503, límite temporal configurable, listas
JSON y modificaciones en sus elementos. Una comprobación offline usa el modelo
del proveedor Oracle y genera el SQL real sin abrir conexión. No se ejecutó esta
migración contra un servidor MySQL: esa aplicación queda pendiente de la revisión
del SQL. Los endpoints públicos `/api/games` están disponibles desde la fase 3; las
sondas usadas aquí solo se registran en las pruebas de catálogo.

## Endpoints del catálogo — fase 3 (US-20 y US-21)

Ambos endpoints requieren un access token válido de cualquier rol y comparten la
política `catalog`: 30 solicitudes por minuto **por usuario**, configurable. Un
cambio de token o de IP no renueva el presupuesto; usuarios detrás de la misma IP
tienen presupuestos independientes. Las solicitudes sin token o con JWT inválido
devuelven 401 sin consumir el presupuesto de un usuario autenticado. El rechazo
429 incluye `code=rate_limited` y `Retry-After` en segundos.

| Endpoint | Contrato y efectos |
|---|---|
| `GET /api/games/search?q=...&page=1&pageSize=20` | q obligatorio, 2–100 caracteres tras recortar; page ≥ 1; pageSize 1–40. Devuelve `{ items, page, pageSize, hasMore }`; cada item contiene `externalId`, `name`, `slug`, `coverUrl`, `releaseYear` nullable y hasta cinco plataformas. No persiste juegos. |
| `GET /api/games/{externalId}` | Entero positivo. Usa `GameService`; devuelve `id` **local**, `externalId`, `name`, `slug`, `summary` en texto plano, `coverUrl`, `coverUrlLarge`, `releaseDate` nullable, `platforms`, `genres`, `developers`. Puede guardar/refrescar una ficha, o servir la copia local ante una caída. |

La validación de búsqueda o paginación usa 400 `invalid_query`. Un ID inválido,
incluidos texto y overflow de `int`, devuelve 400 `invalid_game_id` (decisión de
contrato donde solo se había especificado el estado). Juego inexistente: 404
`game_not_found`. Proveedor caído/deshabilitado sin copia de detalle: 503
`catalog_unavailable`. Si hay copia local, la caída del proveedor no impide abrir
ese detalle; una búsqueda sigue necesitando el proveedor. `extensions.code` se
serializa como `code` en la raíz del JSON. La API envía `Cache-Control: no-store`;
la caché interna del proveedor sigue funcionando.

Swagger agrupa ambas operaciones bajo **Games**, con Bearer, parámetros,
respuestas tipadas y ejemplos ficticios. Documenta los códigos anteriores y
`Retry-After`. El parámetro q aparece obligatorio y aclara que su longitud se
valida después de recortar. No se agregan endpoints públicos de prueba.

### Prueba manual con el proveedor falso

La migración `AddGames` sigue **pendiente de aplicar**. Después de revisar el
[SQL](sql/20261025065759_AddGames.sql), aplicala explícitamente con la conexión
habitual de desarrollo; este paso modifica tu base:

```powershell
dotnet ef database update --project backend/src/Playtrough.Api --startup-project backend/src/Playtrough.Api --configuration Release -- --environment Development
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Catalog__UseFake = "true"
$env:RawgSettings__Enabled = "false"
dotnet run --project backend/src/Playtrough.Api
```

Usá una cuenta existente con email confirmado. Obtené un access token con
`POST /api/auth/login` en Swagger y copialo en **Authorize**, o en la variable
`accessToken` de [Games.http](Games.http). No versiones tokens reales. El archivo
incluye búsqueda, paginación, detalle repetible, ID inexistente, validaciones,
petición sin token y prueba de clave inválida.

1. Buscá `witcher` con `pageSize=2`: dos elementos y `hasMore=true`; la página 2
   devuelve uno y `hasMore=false`. La búsqueda no crea filas en `Games`.
2. Abrí `/api/games/1`: devuelve un ID local y datos ficticios. Repetí: conserva el
   mismo ID, sin duplicar filas. Las imágenes son null.
3. Abrí `/api/games/2147483647`: con el falso, 404 `game_not_found`.
4. Probá búsqueda `q=x`, detalle `/abc` y una solicitud sin JWT: 400
   `invalid_query`, 400 `invalid_game_id` y 401, respectivamente.
5. Para comprobar el 429 sin esperar 30 intentos, podés configurar temporalmente
   `RateLimit__Catalog__PermitLimit=2` y reiniciar: una búsqueda y un detalle del
   mismo usuario consumen el presupuesto; la siguiente petición devuelve 429 y
   `Retry-After`. Quitá esa variable al terminar y reiniciá para volver al valor base.

### Prueba manual con RAWG real

Conservá la clave válida **solo** en user-secrets o `RawgSettings__ApiKey`. Usá una
base de desarrollo sin fichas ficticias para los IDs elegidos: el falso utiliza
IDs 1–20 en el mismo espacio `rawg`; una ficha fresca se sirve localmente aunque
cambies de proveedor de desarrollo.

```powershell
$env:Catalog__UseFake = "false"
$env:RawgSettings__Enabled = "true"
# Configurá tu clave privada con user-secrets como se explica en la fase 1.
dotnet run --project backend/src/Playtrough.Api
```

Buscá un título, tomá un `externalId` del resultado y pedí su detalle. Repetir el
detalle conserva el ID local. Usá un ID que RAWG indique inexistente (por ejemplo,
`2147483647`) para comprobar 404; la respuesta depende del proveedor real. Para la
clave inválida, reemplazá temporalmente **la fuente de configuración activa** por
una cadena deliberadamente inválida, reiniciá y ejecutá la búsqueda de prueba de
`Games.http`: un 401/403 de RAWG se convierte en 503 `catalog_unavailable`, sin
reintento ni exposición de la clave. No uses un detalle local para esa prueba,
porque puede devolver 200 por fallback. Restaurá la clave válida y reiniciá.

Los tests de esta fase usan `WebApplicationFactory`, SQLite aislado y un proveedor
falso que registra llamadas. Conservan login, Identity y validación JWT reales.
Cubren roles, parámetros, contratos JSON, ausencia de persistencia en búsquedas,
detalle y concurrencia, fallback, errores, rate limiting por usuario y Swagger.
No llaman a RAWG. Solo se adapta el test general existente de etiquetas Swagger
para reconocer Games. En la línea de base se observó un fallo intermitente previo
en `RawgResilienceTests.SharedLimiter_PacesConcurrentIndependentClients_AndRetries`;
pasó al repetirlo aislado. No se modificó ese test ni el limitador del proveedor.

## Automated integration tests

The test project includes WebApplicationFactory tests using the real API startup,
Identity, repositories, token service and JWT bearer middleware. Each test owns an
isolated SQLite database in memory and a random JWT signing key. No Docker instance,
MySQL database or development credentials are needed.

```powershell
dotnet test backend/Playtrough.sln -c Release --disable-build-servers -m:1
```

Release allows these tests to run while the development API is open in Debug.
The `__tests/protected` controller is registered only by LoginApiFactory and is not
part of the production API. Tests cover actual lockout/counter updates and tokens
obtained through HTTP login. The expired-token case uses a valid test signature and
past timestamps, so it specifically verifies lifetime validation without waiting.

SQLite exercises persistence and the HTTP pipeline, but does not verify MySQL-specific
behavior or migrations. The test runner reports the current case count.

`AuthServiceLoginTests` and `TokenServiceTests` retain the existing service coverage.
`AuthServiceLogoutTests` uses isolated SQLite databases and the real token service and
refresh-token repository to verify persisted revocation, idempotency, unknown tokens,
ownership and expiry. SQLite supports the repository's `ExecuteUpdateAsync`, unlike
EF InMemory. Identity dependencies that logout does not use are mocked with Moq.

`AuthEndpointsTests` verifies login by email/username, generic credential errors,
validation, lockout and counter reset; signed, tampered, unsigned and expired JWTs;
and logout responses (204/400/401), persisted revocation and repeated logout.

## Refresh rotation

POST /api/auth/refresh is public and requires a nonempty `refreshToken`. Successful
responses have the same token/user fields as login. Invalid, expired, revoked or
unusable-user tokens return the same 401 ProblemDetails. Missing tokens return 400;
unexpected persistence/generation failures return 500 without exposing token values.

Refresh reuses TokenService hashing/generation, RefreshToken.IsActive and JwtSettings.
Consumption, insertion and ReplacedByToken linkage commit in one transaction.
The first SQL operation in that transaction conditionally consumes the active token,
so concurrent refreshes cannot both succeed. Reusing a rotated token revokes the
user's active refresh tokens; reusing a logout-revoked token does not affect others.
The strict reuse policy also applies to concurrent requests: one gets 200 and one
gets 401, and the replay invalidates the winner's new refresh token. Access JWTs
remain valid until their configured expiry.

`AuthServiceRefreshTests` verifies rotation, hashing/lifetimes, current roles,
eligibility, reuse isolation, safe warning logs and transaction rollback.
`RefreshEndpointsTests` exercises the real HTTP flow and concurrency. Ordinary tests
reuse isolated in-memory SQLite; concurrency uses a temporary SQLite WAL database
with separate connections so requests do not share a transaction/connection.
Existing login/logout/register test cases remain unchanged.

MySQL was checked with 28 assertions, including three concurrent request pairs,
RevokedAt/ReplacedByToken persistence, generic errors and logout-session isolation.
The unique temporary account and its tokens were removed afterward. Existing
RefreshTokens fields suffice; no migration is required.

## Swagger / OpenAPI

Se conserva Swashbuckle.AspNetCore 10.2.3. El documento es `v1`, con título
`Playtrough API` y descripción en español. UI, documento y recursos estáticos se
publican solo si se cumplen ambas condiciones: entorno `Development` y
`Swagger:Enabled=true`. La configuración base y el archivo de ejemplo usan `false`;
`appsettings.Development.json` lo activa. Fuera de Development, incluso una
configuración explícita `true` mantiene Swagger desactivado. Las rutas `/swagger`
responden 404 sin exigir JWT cuando está desactivado; HTTP en producción conserva
su redirección a HTTPS previa a ese control.

Las operaciones se agrupan en **Auth** (registro, login, logout, refresh,
recuperación y confirmación), **Admin** (usuarios, roles y auditoría) y **Games**
(búsqueda y detalle del catálogo). Se excluyen
`/api/test/*` y los controladores `/__tests/*` que registra únicamente la fixture.
Los resúmenes, reglas y respuestas se documentan con comentarios XML y
`ProducesResponseType`; el proyecto genera `Playtrough.Api.xml` y Swagger lo incluye.
Los errores tienen schemas de ProblemDetails o ValidationProblemDetails, y cada
operación documenta los estados que le corresponden, incluidos 413 y 429. El 204 de
logout no incluye body. Los 429 documentan también el header `Retry-After`.

El esquema HTTP **Bearer (JWT)** habilita **Authorize**. Pegá únicamente el
`accessToken`, sin el prefijo `Bearer`: Swagger UI agrega el header automáticamente.
El requisito de seguridad se declara en logout y Admin. Los endpoints públicos
de Auth no tienen candado ni requisito Bearer. El filtro también respeta la
protección por defecto de los endpoints sin atributos de autorización.

Registro, login, refresh y cambio de rol incluyen ejemplos de request/response en
sus DTOs y en las operaciones. Los ejemplos existentes de login por email/username
y de rotación se conservan. Registro muestra `emailConfirmationRequired`; login
incluye el 403 por email sin confirmar. Todos los valores son ilustrativos y los
tokens de ejemplo no sirven para autenticar ni renovar sesiones.

`extensions.code` se serializa como `code` en la raíz del ProblemDetails. Se
documentan los códigos que ya emite cada flujo:

| Código | Estado | Situación |
|---|---|---|
| `email_not_confirmed` | 403 | Login con email sin confirmar |
| `invalid_role` | 400 | Rol inválido en un cambio de rol |
| `cannot_change_own_role` | 400 | Un admin intenta cambiar su propio rol |
| `last_admin` | 400 | Se intenta degradar al último admin |
| `invalid_role_state` | 400 | El usuario no tiene exactamente un rol soportado |
| `user_not_found` | 404 | El usuario de destino no existe |
| `role_change_failed` | 500 | Falló el cambio de rol |
| `rate_limited` | 429 | Presupuesto de solicitudes agotado |
| `payload_too_large` | 413 | Body mayor al límite configurado |
| `internal_error` | 500 | Excepción no controlada fuera de Development |
| `invalid_query` | 400 | Búsqueda de juegos o paginación inválida |
| `invalid_game_id` | 400 | ID externo de juego inválido |
| `game_not_found` | 404 | El proveedor informa que el juego no existe |
| `catalog_unavailable` | 503 | Catálogo caído/deshabilitado y sin copia local de detalle |

Para probar desde Swagger en Development:

1. Abrí `/swagger` y verificá las etiquetas Auth, Admin y Games. `/swagger/v1/swagger.json`
   entrega el documento sin rutas de prueba.
2. Usá login con una cuenta de prueba cuyo email esté confirmado. Copiá el access
   token en **Authorize** y usá logout o, con rol Admin, el listado de usuarios.
3. En refresh, enviá el refresh token vigente del login o de la última renovación.
   Guardá ambos tokens nuevos; repetir el anterior activa la detección de reutilización.
4. En Development, configurá `Swagger:Enabled=false`, reiniciá y comprobá el 404
   de UI y documento. Fuera de Development permanecen desactivados.

`SwaggerDocumentationTests` comprueba disponibilidad por entorno/configuración,
Bearer público/protegido y fallback, metadata, etiquetas, exclusión de pruebas,
respuestas tipadas, códigos, header Retry-After y ejemplos. Los tests anteriores de
Swagger ahora usan Development y verifican la seguridad por operación. Las pruebas
de negocio, roles y sesiones conservan su comportamiento.

## Password recovery

POST /api/auth/forgot-password accepts a valid email and always returns the same 200
message for existing, unknown and locked accounts. Account lookup, Identity token
generation and email delivery run in a scoped background worker. Its in-memory queue
is not durable and does not retry failed messages. Delivery errors are logged without
email addresses, tokens, message bodies or exception details.

POST /api/auth/reset-password accepts email, token, newPassword and confirmPassword.
Invalid, expired, reused and wrong-account links return the same 400 ProblemDetails;
password-policy failures expose the applicable Identity validation rules. A successful
reset changes the security stamp, revokes the user's active refresh tokens, clears
failed attempts and removes lockout in one transaction. Existing access JWTs retain
their normal lifetime. No schema changes or migrations are needed.

Recovery links target the configured frontend /reset-password route. The frontend
screen is outside this backend change. Identity's existing default token providers
are reused, with a configurable 60-minute lifetime and URL-safe Base64 encoding.

The recovery service and endpoint tests use real Identity, isolated SQLite databases,
ephemeral data-protection keys and a mocked IEmailSender. They cover single use,
expiry, password policy, account isolation, unlock, refresh revocation, background
delivery failures and configuration. A local MailKit/MailHog/MySQL check also passed
23 assertions; its temporary account, tokens and email were removed afterward.

## Configuración y secretos

El proyecto ya tiene `UserSecretsId`. En **Development**, guardá los secretos con
`dotnet user-secrets`; en **producción**, inyectalos como variables de entorno
desde la configuración privada del servicio. Las variables de entorno tienen
precedencia sobre JSON y user-secrets. No se lee automáticamente un archivo `.env`
ni `appsettings.*.local.json` desde la API. `.env` se usa por Docker Compose.

Desde la raíz del repositorio, el formato para cualquier clave de la tabla es:

```powershell
dotnet user-secrets set 'CLAVE_DE_LA_TABLA' 'VALOR_LOCAL' --project backend/src/Playtrough.Api
```

Reemplazá los marcadores personalmente. Los comandos con valores sensibles pueden
quedar en el historial del shell: también podés editar el almacén local de
user-secrets desde tu IDE. No pegues su contenido ni la salida de `user-secrets list`
en documentación, chats o logs. User-secrets queda fuera del repo, pero no está
cifrado y se usa solamente para desarrollo.

Para producción, usá el nombre de la tercera columna; `__` representa `:`.
Los ejemplos son marcadores, nunca credenciales utilizables:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=HOST_MYSQL;Database=BASE_MYSQL;User ID=USUARIO_MYSQL;Password=CONTRASENA_LOCAL;'
$env:Jwt__Key = 'REEMPLAZAR_POR_CLAVE_ALEATORIA_PRIVADA'
$env:PasswordReset__FrontendBaseUrl = 'https://frontend.example.com'
```

| Clave para `user-secrets set` | Uso y requisitos | Variable de entorno |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Conexión MySQL obligatoria, con servidor, base y usuario; puerto 1–65535. Contraseña según el servidor. | `ConnectionStrings__DefaultConnection` |
| `Jwt:Key` | Clave privada aleatoria de al menos 32 bytes UTF-8; no usar placeholders ni frases previsibles. | `Jwt__Key` |
| `Jwt:Issuer` | Emisor obligatorio; valor público predeterminado `Playtrough`. | `Jwt__Issuer` |
| `Jwt:Audience` | Audiencia obligatoria; valor público predeterminado `PlaytroughClient`. | `Jwt__Audience` |
| `Jwt:AccessTokenExpirationMinutes` | Vigencia de acceso, 1–60 minutos; predeterminado 15. | `Jwt__AccessTokenExpirationMinutes` |
| `Jwt:RefreshTokenExpirationDays` | Vigencia del refresh, 1–30 días; predeterminado 7. | `Jwt__RefreshTokenExpirationDays` |
| `PasswordReset:FrontendBaseUrl` | URL HTTP(S) absoluta, sin credenciales, query ni fragmento; usada por recuperación y confirmación. En desarrollo: `http://localhost:4200`. | `PasswordReset__FrontendBaseUrl` |
| `PasswordReset:TokenLifespanMinutes` | Vigencia de recuperación, 1–1440 minutos; predeterminado 60. | `PasswordReset__TokenLifespanMinutes` |
| `EmailConfirmation:TokenLifespanMinutes` | Vigencia de confirmación, 1–10080 minutos; predeterminado 60. | `EmailConfirmation__TokenLifespanMinutes` |
| `EmailConfirmation:ResendCooldownSeconds` | Espera entre reenvíos, 1–86400 segundos; predeterminado 60. | `EmailConfirmation__ResendCooldownSeconds` |
| `Email:Enabled` | Habilita SMTP; predeterminado `true`. `false` conserva el modo existente sin envío. | `Email__Enabled` |
| `Email:Host` | Servidor obligatorio con correo habilitado; MailHog local en desarrollo o Gmail explícito. | `Email__Host` |
| `Email:Port` | Puerto SMTP obligatorio, 1–65535; MailHog 1025, Gmail 587. | `Email__Port` |
| `Email:Username` | Usuario SMTP; vacío para MailHog, requerido para Gmail. | `Email__Username` |
| `Email:Password` | Credencial SMTP, obligatoria si hay usuario; contraseña de aplicación para Gmail. | `Email__Password` |
| `Email:FromAddress` | Remitente válido obligatorio con correo habilitado; para Gmail debe coincidir con el usuario. | `Email__FromAddress` |
| `Email:FromName` | Nombre visible del remitente, opcional. | `Email__FromName` |
| `Email:SecureSocketMode` | `None`, `StartTls` o `SslOnConnect`; Gmail exige `StartTls`, MailHog local `None`. | `Email__SecureSocketMode` |
| `Email:TimeoutSeconds` | Timeout SMTP, 1–120 segundos; predeterminado 15. | `Email__TimeoutSeconds` |
| `AdminSeed:Username` | Usuario del admin inicial; configuración opcional. | `AdminSeed__Username` |
| `AdminSeed:Email` | Correo del admin inicial; requiere los otros dos campos del seed. | `AdminSeed__Email` |
| `AdminSeed:Password` | Contraseña del admin inicial; privada, nunca versionada. El seed existente no modifica cuentas existentes. | `AdminSeed__Password` |
| `Cors:AllowedOrigins:0` | Primer origen autorizado, sin comodines; los siguientes usan índices `:1`, `:2`, etc. Sin orígenes configurados no se concede acceso CORS. | `Cors__AllowedOrigins__0` |
| `Transport:MaxRequestBodySizeBytes` | Máximo del cuerpo de las solicitudes, positivo; predeterminado 1048576 bytes (1 MiB). | `Transport__MaxRequestBodySizeBytes` |
| `Transport:HttpsPort` | Puerto de destino de la redirección HTTPS fuera de Development; predeterminado 443. | `Transport__HttpsPort` |
| `Transport:ForwardLimit` | Cantidad máxima de saltos de proxy que se procesan; positivo, predeterminado 1. | `Transport__ForwardLimit` |
| `Transport:KnownProxies:0` | IP del primer proxy confiable; lista vacía por defecto. Los siguientes usan índices `:1`, `:2`, etc. | `Transport__KnownProxies__0` |
| `Transport:KnownNetworks:0` | Primera red confiable en formato CIDR, por ejemplo `10.20.0.0/16`; lista vacía por defecto. | `Transport__KnownNetworks__0` |
| `RateLimit:Enabled` | Activa todos los límites; predeterminado `true`. | `RateLimit__Enabled` |
| `RateLimit:Login:PermitLimit` / `RateLimit:Login:Window` | Intentos por IP; predeterminado 10 en `00:01:00`. | `RateLimit__Login__PermitLimit` / `RateLimit__Login__Window` |
| `RateLimit:Register:PermitLimit` / `RateLimit:Register:Window` | Registros por IP; predeterminado 5 en `01:00:00`. | `RateLimit__Register__PermitLimit` / `RateLimit__Register__Window` |
| `RateLimit:PasswordRecovery:PermitLimit` / `RateLimit:PasswordRecovery:Window` | Presupuesto compartido de recuperación y reenvío por IP; 5 en `00:15:00`. | `RateLimit__PasswordRecovery__PermitLimit` / `RateLimit__PasswordRecovery__Window` |
| `RateLimit:TokenActions:PermitLimit` / `RateLimit:TokenActions:Window` | Presupuesto compartido de reset y confirmación por IP; 10 en `00:15:00`. | `RateLimit__TokenActions__PermitLimit` / `RateLimit__TokenActions__Window` |
| `RateLimit:Refresh:PermitLimit` / `RateLimit:Refresh:Window` | Renovaciones por IP; predeterminado 30 en `00:01:00`. | `RateLimit__Refresh__PermitLimit` / `RateLimit__Refresh__Window` |
| `RateLimit:Catalog:PermitLimit` / `RateLimit:Catalog:Window` | Búsqueda y detalle compartidos por usuario; predeterminado 30 en `00:01:00`. | `RateLimit__Catalog__PermitLimit` / `RateLimit__Catalog__Window` |
| `RateLimit:Global:PermitLimit` / `RateLimit:Global:Window` | Resto de solicitudes por usuario autenticado o IP anónima; 100 en `00:01:00`. | `RateLimit__Global__PermitLimit` / `RateLimit__Global__Window` |
| `Swagger:Enabled` | Activa UI y documento únicamente en Development; `true` por defecto en ese entorno y `false` en la base. | `Swagger__Enabled` |
| `RawgSettings:Enabled` | Habilita RAWG; predeterminado `false`, permite arrancar sin clave. | `RawgSettings__Enabled` |
| `RawgSettings:ApiKey` | Clave privada, obligatoria si RAWG está habilitado; solo user-secrets o entorno, nunca archivos versionados. | `RawgSettings__ApiKey` |
| `RawgSettings:BaseUrl` | Base HTTPS sin credenciales, query ni fragmento; `https://api.rawg.io/api`. | `RawgSettings__BaseUrl` |
| `RawgSettings:MaxRequestsPerSecond` | Máximo compartido por todas las llamadas y reintentos; predeterminado 5 (1–1000). | `RawgSettings__MaxRequestsPerSecond` |
| `RawgSettings:RequestTimeoutSeconds` | Timeout por intento, incluido el cuerpo; predeterminado 10 (1–120). | `RawgSettings__RequestTimeoutSeconds` |
| `RawgSettings:SearchCacheMinutes` | Duración de búsquedas en memoria; predeterminado 10 (1–525600). | `RawgSettings__SearchCacheMinutes` |
| `RawgSettings:DetailCacheMinutes` | Duración de detalles en memoria; predeterminado 1440, 24 horas (1–525600). | `RawgSettings__DetailCacheMinutes` |
| `Catalog:UseFake` | Catálogo de ejemplo, predeterminado `false`. Solo Development; en otros entornos impide arrancar. | `Catalog__UseFake` |
| `Catalog:RefreshAfterDays` | Refrescar fichas locales con más de N días; predeterminado 30, rango 1–3650. Si el proveedor falla, devolver la copia local sin cambiar su fecha. | `Catalog__RefreshAfterDays` |
| `Logging:LogLevel:Default` | Nivel general de logs; predeterminado `Information`. | `Logging__LogLevel__Default` |
| `Logging:LogLevel:Microsoft.AspNetCore` | Nivel de ASP.NET Core; predeterminado `Warning`. | `Logging__LogLevel__Microsoft.AspNetCore` |
| `AllowedHosts` | Filtro de hosts de ASP.NET Core; configurar los hosts del servicio según el despliegue. | `AllowedHosts` |

`appsettings.Example.json` enumera todas estas opciones con secretos vacíos y datos
ilustrativos. No se carga automáticamente en Development ni Production. Conservamos
los nombres actuales (`Jwt`, `Email`, `DefaultConnection`), sin crear aliases como
`JwtSettings`, `EmailSettings` o `Default`.

Al iniciar se validan las opciones **antes de acceder a la base para el seed**.
Un error identifica la clave y el requisito, sin exponer su valor. La clave JWT
corta, vacía o reconocida como placeholder se rechaza también en Development.
La validación de conexión comprueba sintaxis y campos, no disponibilidad del servidor
ni vigencia de sus credenciales. Las duraciones mantienen sus valores predeterminados.
El correo desactivado y el admin inicial opcional conservan su comportamiento previo.

Las contraseñas y claves JWT deben permanecer vacías en los `appsettings` versionados.
`CommittedConfigurationTests` controla todos esos archivos mediante Git al ejecutar
la suite, sin mostrar valores en los errores. `StartupConfigurationTests` verifica
errores de arranque y ausencia de filtraciones; `AuthLoggingTests` verifica los logs
de login y registro. Ver [auditoría y credenciales a rotar](SECURITY_AUDIT.md).

### Configuración existente

The API keeps its existing UserSecretsId, so local database credentials and the JWT
signing key remain available after the project rename. Set `ConnectionStrings:DefaultConnection`
and `Jwt:Key` through user-secrets, or use `ConnectionStrings__DefaultConnection` and
`Jwt__Key` environment variables. Never commit secrets.

JWT issuer/audience are now `Playtrough` and `PlaytroughClient`. Previously issued
tokens require a new login after restarting the API.

Set PasswordReset:FrontendBaseUrl to the frontend's absolute HTTP(S) URL;
PasswordReset:TokenLifespanMinutes defaults to 60.

## Transporte, CORS y errores

Development permite por defecto `http://localhost:4200`; la configuración base
no contiene orígenes. Se autorizan solamente `GET`, `POST`, `PUT`, `DELETE`, `OPTIONS`
y los headers `Authorization`, `Content-Type`, sin credenciales de cookies. Los
comodines y las URLs que no representen un origen se rechazan al iniciar. CORS se
ejecuta antes de autenticación y autorización, incluso para preflight protegido.

Fuera de Development, HTTP redirige a HTTPS con 307 y las respuestas HTTPS llevan
HSTS de 30 días (con las exclusiones de loopback de ASP.NET Core). `HttpsPort` indica
el puerto público de destino; no crea un listener ni instala certificados.

Las respuestas de API incluyen `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` y
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`.
El CSP se exceptúa para `/swagger` solo mientras la UI está habilitada en Development.
Las respuestas 404 de Swagger desactivado conservan el CSP de la API.
Kestrel no agrega `Server`.

El máximo de body es 1 MiB y se aplica a los endpoints actuales. Se rechaza el
`Content-Length` excesivo antes de leer y el servidor limita también las lecturas
de cuerpos sin longitud declarada. El rechazo devuelve 413 ProblemDetails con
`code: payload_too_large`. La suite comprueba ambos casos contra Kestrel real,
sin depender de las limitaciones de simulación de TestServer.

Solo se procesan `X-Forwarded-For` y `X-Forwarded-Proto` de IPs/redes explícitamente
configuradas. Sin listas, ambos headers se ignoran, incluso desde loopback y si se
activa la configuración automática de forwarding del framework. Los valores por
defecto de proxies del framework se sustituyen por estas listas; no se confía en
`X-Forwarded-Host`. Forwarding precede a HTTPS, CORS y autenticación. Con múltiples
proxies, configurar los intermediarios y `ForwardLimit` según la cadena real.

Fuera de Development, las excepciones no controladas producen 500 ProblemDetails
con `code: internal_error` y texto genérico en español. El log registra TraceId,
tipo, HResult y traza de cada excepción de la cadena (incluidas las internas).
Se omiten mensajes, `Data` y propiedades arbitrarias porque pueden contener claves,
tokens, conexiones o links completos. El framework no vuelve a registrar la
excepción cruda: se usa la [supresión de diagnósticos de ASP.NET Core 10](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0#suppressdiagnosticscallback).
Development conserva la página de excepciones para depuración.

Los endpoints que lanzan errores o muestran la conexión pertenecen únicamente al
assembly de tests; no forman parte de la API publicada. En TestServer, un
`IStartupFilter` de prueba fija la IP remota antes del pipeline real. Esa IP no se
obtiene de un header enviado por el cliente. La fixture general usa HTTPS y define
explícitamente el origen Angular en el entorno Testing.

Para probar CORS y headers manualmente con la API en Development:

```powershell
curl.exe -i -X OPTIONS 'http://localhost:5000/api/admin/users' -H 'Origin: http://localhost:4200' -H 'Access-Control-Request-Method: GET' -H 'Access-Control-Request-Headers: authorization,content-type'
curl.exe -i -X OPTIONS 'http://localhost:5000/api/admin/users' -H 'Origin: https://unlisted.example' -H 'Access-Control-Request-Method: GET'
curl.exe -i 'http://localhost:5000/health'
```

El primer preflight devuelve 204 con el origen permitido; el segundo no incluye
`Access-Control-Allow-Origin`. `/health` incluye los headers de seguridad y no
incluye `Server`. Ajustá el puerto al indicado por tu instancia.

## Rate limiting

Se usa el [rate limiter integrado de ASP.NET Core](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0),
sin nuevas dependencias. Está habilitado por defecto, con ventanas fijas y rechazo
inmediato cuando se agotan los permisos, sin poner solicitudes en cola.

| Política | Endpoints | Límite predeterminado | Partición |
|---|---|---|---|
| `login` | `POST /api/auth/login` | 10 por minuto | IP |
| `register` | `POST /api/auth/register` | 5 por hora | IP |
| `password-recovery` | `POST /api/auth/forgot-password`, `POST /api/auth/resend-confirmation` | 5 cada 15 minutos, compartidos | IP |
| `token-actions` | `POST /api/auth/reset-password`, `POST /api/auth/confirm-email` | 10 cada 15 minutos, compartidos | IP |
| `refresh` | `POST /api/auth/refresh` | 30 por minuto | IP |
| `catalog` | `GET /api/games/search`, `GET /api/games/{externalId}` | 30 por minuto, compartidos | ID de usuario autenticado |
| `global` | Resto de endpoints, incluidos logout, Admin y `/health` | 100 por minuto, compartidos entre rutas | ID de usuario autenticado; IP para anónimos |

Los endpoints con política propia no consumen el presupuesto global. Las
solicitudes sujetas a un presupuesto consumen un permiso aunque después fallen por
validación o autorización. En catalog, las solicitudes sin identidad autenticada
no consumen permisos y reciben 401. El lockout de Identity permanece: sus respuestas 423 por
cuenta conviven con el 429 por exceso de solicitudes. Los contadores residen en cada
proceso y se reinician al reiniciar la API; las ventanas fijas se renuevan completas,
no calculan una ventana móvil.

La sección `RateLimit` de `appsettings.json` y `appsettings.Example.json` enumera los
límites. Cada política tiene `PermitLimit` positivo y `Window` de tipo TimeSpan
positivo (`hh:mm:ss`, por ejemplo `00:15:00`). Se validan al iniciar sin mostrar
valores inválidos. `RateLimit:Enabled=false` desactiva tanto las políticas propias
como el límite global. Los cambios requieren reiniciar la API. Se pueden cargar
mediante user-secrets o variables de entorno como las demás claves de la tabla.

El orden relevante del pipeline es ForwardedHeaders, routing, CORS, límite de body,
resolución de identidad para los límites global y catalog, rate limiting, autenticación y
autorización. Para respetar el limitador antes de `UseAuthentication` y particionar
por usuario, un middleware consulta el handler Bearer existente para
solicitudes sin política propia o con política catalog. Usa el `sub` solo después de validar el JWT; no
decodifica tokens sin verificar. El handler guarda el resultado por solicitud y
`UseAuthentication` lo reutiliza. Las políticas de autenticación conservan su
límite por IP, incluso si reciben un JWT. CORS resuelve los preflight antes de
consumir permisos y conserva sus headers también en los rechazos 429.

La IP procede de la conexión, con forwarding solo para los proxies/redes confiables
de `Transport`. La misma dirección IPv4 y su representación IPv6 mapeada comparten
presupuesto. Si no existe IP remota, los clientes anónimos comparten la partición
`unknown`. Un JWT inválido o expirado también usa la partición de IP.

El rechazo devuelve `application/problem+json`, estado 429, `code: rate_limited`
(la extensión se serializa en la raíz de ProblemDetails), texto genérico en español
y `Retry-After` en segundos, redondeado hacia arriba. El log de advertencia registra
solo política e IP, sin IDs, credenciales, tokens ni datos del body.

`LoginApiFactory` desactiva los límites para conservar los tests existentes. Los
tests de `RateLimitingEndpointsTests` los habilitan con presupuestos pequeños y un
`IStartupFilter` exclusivo de tests: como TestServer no tiene una conexión TCP real,
el filtro toma `X-Test-Remote-IP` y fija `RemoteIpAddress` antes del pipeline. Este
header no se interpreta en producción. Se prueban las siete rutas, presupuestos
compartidos, aislamiento por IP/usuario, renovación de ventanas, forwarding, CORS,
lockout, logs seguros y modo desactivado. `RateLimitConfigurationTests` comprueba
binding y rechazo de configuraciones inválidas al iniciar.

### Prueba manual

Con la API levantada, límites predeterminados y presupuesto de esa IP disponible,
enviá 11 solicitudes consecutivas a `POST /api/auth/login` con credenciales de una
cuenta ilustrativa inexistente. Lo esperado es diez 401 y luego 429 con
`code: rate_limited` y `Retry-After`; solicitudes previas pueden adelantar el rechazo.
Después de esperar los segundos indicados, se admiten intentos otra vez. No uses
una cuenta real para esta prueba porque los fallos también podrían activar su lockout.

## Configuración del correo

La sección de configuración sigue siendo `Email` (no `EmailSettings`). MailKit usa
`FromAddress`, `FromName` y `SecureSocketMode`; estos nombres reemplazan a
`SenderEmail`, `SenderName` y `Security`. Actualizá cualquier configuración externa
anterior. `IEmailSender`, sus consumidores y las plantillas conservan su contrato.

`Enabled=true` activa SMTP. Al iniciar se validan Host, Port, FromAddress, el modo de
seguridad y las credenciales necesarias. `Enabled=false` omite completamente SMTP y
sus requisitos de configuración; solo deja un log informativo sin datos del mensaje.
El timeout de conexión/intercambio completo es de 15 segundos por defecto,
configurable mediante `Email:TimeoutSeconds` (1 a 120).

### MailHog en desarrollo

Es el modo predeterminado de `appsettings.Development.json`: localhost:1025, sin TLS
ni credenciales. Desde la raíz del repositorio:

```powershell
docker compose -f backend/docker-compose.yml --profile development up -d --wait
dotnet run --project backend/src/Playtrough.Api
```

Bandeja: http://localhost:8025. Ambos puertos están ligados a la interfaz local.
Development permite MailHog local o Gmail explícitamente configurado; otro SMTP remoto
se rechaza. No se conectan servidores reales desde los tests.

`EmailSettingsTests` verifica validación al iniciar, Gmail explícito en Development,
campos obligatorios y modo desactivado. `SmtpEmailSenderTests` usa un transporte
MailKit mockeado para probar mapeo TLS, cuerpos MIME, autenticación condicional,
timeout configurado, liberación del cliente y logs sin datos sensibles.

### Activar Gmail con user-secrets

Usá una cuenta dedicada con verificación en dos pasos y generá una contraseña de
aplicación; no uses la contraseña habitual de la cuenta. La disponibilidad depende
de la política de la cuenta. Consultá la [ayuda oficial de Google](https://support.google.com/accounts/answer/185833?hl=es).

Desde la raíz, reemplazá los placeholders y ejecutá estos comandos personalmente:

```powershell
dotnet user-secrets set "Email:Enabled" "true" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:Host" "smtp.gmail.com" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:Port" "587" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:SecureSocketMode" "StartTls" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:Username" "TU_CUENTA@gmail.com" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:Password" "TU_CONTRASENA_DE_APLICACION" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:FromAddress" "TU_CUENTA@gmail.com" --project backend/src/Playtrough.Api
dotnet user-secrets set "Email:FromName" "Playtrough" --project backend/src/Playtrough.Api
```

Reiniciá la API en Development. User-secrets sobreescribe los valores de MailHog.
El proyecto ya tiene UserSecretsId. Gmail exige aquí puerto 587 con STARTTLS,
credenciales no vacías y FromAddress igual a Username. Verificá la configuración
SMTP en la [documentación de Google](https://knowledge.workspace.google.com/admin/gmail/send-email-from-a-printer-scanner-or-app).

### Volver a MailHog

Quitá solo las claves de correo para conservar los secretos existentes de MySQL/JWT:

```powershell
$correoClaves = 'Enabled', 'Host', 'Port', 'SecureSocketMode', 'Username', 'Password', 'FromAddress', 'FromName', 'TimeoutSeconds'
foreach ($correoClave in $correoClaves) {
  dotnet user-secrets remove "Email:$correoClave" --project backend/src/Playtrough.Api
}
```

Reiniciá la API. Si habías definido variables de entorno Email__*, retiralas también:
las variables tienen prioridad sobre user-secrets y los archivos JSON. No uses
`user-secrets clear`, porque eliminaría también los secretos ajenos al correo.

### Servidor / producción

User-secrets se carga por defecto solo en Development. En el servidor configurá las
variables mediante su gestor de secretos; este ejemplo PowerShell usa placeholders:

```powershell
$env:Email__Enabled = 'true'
$env:Email__Host = 'smtp.gmail.com'
$env:Email__Port = '587'
$env:Email__SecureSocketMode = 'StartTls'
$env:Email__Username = 'TU_CUENTA@gmail.com'
$env:Email__Password = 'TU_CONTRASENA_DE_APLICACION'
$env:Email__FromAddress = 'TU_CUENTA@gmail.com'
$env:Email__FromName = 'Playtrough'
$env:Email__TimeoutSeconds = '15'
$env:PasswordReset__FrontendBaseUrl = 'https://TU_FRONTEND'
```

Host y remitente quedan vacíos en el archivo base para exigir configuración explícita.
Las credenciales reales no se guardan en JSON, README ni Git. El archivo .env local
está ignorado; .env.example contiene únicamente placeholders.

### Errores habituales de Gmail

- `534-5.7.9` (también `534 5.7.90`): se requiere contraseña de aplicación;
  habilitá verificación en dos pasos y comprobá que la cuenta permita generarla.
- `535-5.7.8` (también `535 5.7.80`): autenticación rechazada; revisá usuario y
  contraseña de aplicación, que puede haber sido revocada tras cambiar la contraseña.
- `550-5.4.5`: cuota diaria superada. Gmail personal documenta límites de más de 500
  mensajes por día o más de 500 destinatarios en un mensaje; Workspace y SMTP relay
  tienen cuotas diferentes. El código no implica por sí solo una cuota universal de 500.

Fuentes: [códigos SMTP de Google](https://support.google.com/mail/answer/3726730?hl=en)
y [límites de Gmail personal](https://support.google.com/mail/answer/22839?hl=es).

Los logs registran el tipo de excepción y un mensaje sanitizado (con código SMTP
numérico cuando está disponible), nunca la respuesta completa del servidor, la
contraseña, el destinatario ni el cuerpo/enlace. El servicio propaga el fallo según
su contrato; el flujo existente de recuperación lo captura en segundo plano y
conserva la respuesta neutra al cliente. No hay reintentos automáticos.

## Existing local database

The database `playtrought_dev`, Docker container `playtrought-mysql` and Compose volume
key `playtrought_mysql_data` retain their original infrastructure identifiers to keep
using the existing data. They are not public application names. No database objects,
data, migration IDs or volume contents are renamed by the application name correction.

To start MySQL using the existing local `backend/.env`:

```powershell
docker compose -f backend/docker-compose.yml up -d --wait
```
