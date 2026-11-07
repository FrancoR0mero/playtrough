# Épica 9 — fase 1: perfiles

Estado: implementación de fase 1. Migración generada; **no aplicada**.

## API

Todos los endpoints requieren un access token válido, de cualquier rol.

- `GET /api/users/me`: perfil propio, incluido email y próximo cambio de username.
- `GET /api/users/{username}`: búsqueda por nombre normalizado de Identity; sin email ni id interno. Las cuentas sin confirmar devuelven `404 user_not_found`, excepto al dueño.
- `PUT /api/users/me`: edición parcial de `username` y `bio`. La identidad proviene del token. Campos omitidos se conservan; `bio: null` la elimina. JSON de tipos incorrectos devuelve los códigos de perfil.

`friendsCount` y `pendingReceivedCount` valen cero, la relación es `none` sin `requestId`, y el perfil no está restringido en esta fase. `avatarUrl` es null. `me` y `search` quedan excluidos de la ruta variable; `search` se implementará en la fase 3.

La política `profile-write` usa `RateLimit:ProfileWrite` (10 solicitudes por minuto y usuario), con `429 rate_limited` y `Retry-After`. Un token nuevo del mismo usuario conserva el presupuesto.

## Decisiones

- Se conserva el proveedor instalado `MySql.EntityFrameworkCore` 10.0.9; el repositorio no usa Pomelo.
- `Profile:UsernameChangeCooldownDays` vale 30. `Profile:ReservedUsernames` contiene la lista acordada y se comparte entre registro y edición. Las cuentas existentes con nombres reservados siguen funcionando.
- Los nombres nuevos solo admiten letras ASCII, números y `_`, de 3 a 20 caracteres. El registro antes permitía puntos; se ajustó a la nueva regla sin renombrar cuentas existentes.
- Un nombre idéntico, ignorando mayúsculas, es una operación sin cambios: conserva la capitalización y la fecha anterior. Los cambios efectivos se guardan con `UserManager`, incluyendo normalización, bio y fecha en una sola escritura. Identity controla concurrencia; los conflictos no debidos al plazo se comunican como `409 concurrent_modification`.
- El saneamiento se extrajo de reseñas sin alterar su comportamiento. El límite de bio se mide después del saneamiento en valores escalares Unicode: un emoji simple cuenta como un carácter. Se conserva texto como `<b>texto</b>` literalmente; no se interpreta como HTML.
- El reloj de perfiles y la fecha de registro usan `TimeProvider`. El resto del sistema de autenticación conserva sus relojes existentes.
- El refresh ya consulta el usuario en la base antes de emitir claims. El frontend debe forzarlo tras cambiar el username; hay un test HTTP que valida el JWT resultante.
- `CreatedAt` ya existía. Su migración histórica (`RenameIdentityTablesAndAddUserCreatedAt`) asignó `0001-01-01` a cuentas anteriores. Se conserva ese dato, sin inventar fechas ni corregir datos históricos fuera del alcance acordado.

## Migración

Desde `20261104191806_AddGameBrainDetailsCache` hasta `20261107232526_AddUserProfiles`.
SQL revisable en `backend/sql/20261107232526_AddUserProfiles.sql`:

```sql
ALTER TABLE `Users` ADD `Bio` varchar(300) CHARACTER SET utf8mb4 NULL;
ALTER TABLE `Users` ADD `UsernameChangedAt` datetime(6) NULL;
```

La migración no modifica filas existentes: ambas columnas comienzan en null. El SQL completo incluye el registro en `__EFMigrationsHistory`. Se generó en modo offline sin conexión a la base. MySQL puede realizar commits implícitos para DDL aunque el script incluya `START TRANSACTION`.

## Verificación y prueba manual

Los tests reutilizan `GamesApiTestContext`, Identity real, JWT reales y SQLite aislado. Cubren roles, ocultamiento de cuentas sin confirmar, campos de respuestas, edición parcial, nombres reservados configurables, unicidad sin distinguir mayúsculas, reloj y límite exacto, saneamiento y emojis, refresh, 401, rate limiting y Swagger. Un test genera SQL con el proveedor MySQL y verifica que el snapshot no tenga cambios pendientes; no ejecuta la migración sobre MySQL.

```powershell
dotnet build backend/Playtrough.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/Playtrough.sln --no-build -m:1
```

Se usa un solo proceso MSBuild y compilación sin servidor porque el entorno de ejecución interrumpía la compilación compartida. Testhost requiere ejecutarse fuera del sandbox para establecer su conexión local.

Después de aprobar y aplicar la migración, usar `backend/requests/profiles.http` con access tokens de tres cuentas confirmadas. Las pruebas manuales de amistades, búsqueda y privacidad se agregarán con sus respectivas fases.
