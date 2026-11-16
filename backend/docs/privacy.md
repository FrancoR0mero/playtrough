# Épica 9 — fase 4: privacidad

## Contrato

- `GET /api/users/me/privacy`: `{ profileVisibility, historyVisibility }`.
- `PUT /api/users/me/privacy`: ambos campos obligatorios, cada uno exactamente `public`, `friends` o `private`. Devuelve 200 con los valores guardados. Ausentes, null, mayúsculas, números, valores desconocidos y cuerpos inválidos devuelven `400 invalid_visibility`. La actualización es atómica y toma el usuario del token.
- GET de privacidad usa el límite global existente. PUT comparte `profile-write` con PUT del perfil; no agrega otro presupuesto. Todos requieren autenticación de cualquier rol.
- `GET /api/users/{username}` mantiene la respuesta completa cuando el perfil es visible. Si no, devuelve 200 con únicamente `username`, `avatarUrl`, `relationship`, `isRestricted: true` y `restriction: private | friends_only`.
- El servicio conserva bio, fecha y contador en null para un perfil restringido. El controlador usa un DTO específico que omite esos campos del JSON, siguiendo el contrato más estricto pedido por los tests. En perfiles visibles una bio vacía sigue apareciendo como null.
- `GET /api/users/{username}/friends` reutiliza el listado paginado de amigos (page 1, pageSize 20, máximo 50, orden alfabético con desempate por id). Respeta la visibilidad del **perfil**, no del historial. Responde `403 profile_restricted` si está restringido, `404 user_not_found` para inexistentes o cuentas no confirmadas ajenas y `400 invalid_query` para paginación inválida. El dueño siempre puede acceder.
- Search sigue mostrando username y avatar de cuentas confirmadas independientemente de su privacidad. Un perfil privado puede recibir solicitudes.

## Acceso

| Visor | public | friends | private |
| --- | --- | --- | --- |
| Dueño | Sí | Sí | Sí |
| Amigo | Sí | Sí | No |
| Otro usuario autenticado | Sí | No | No |

La matriz se aplica tanto a `PrivacyScope.Profile` como a `PrivacyScope.History`. `IPrivacyService.CanViewAsync(viewerId, ownerId, scope)` consulta valores actuales sin tracking ni caché e incluye como máximo una consulta de existencia de amistad en la misma sentencia SQL. Para el dueño, EF elimina esa comprobación innecesaria.

El perfil ya consulta la relación para mostrarla al frontend. En ese caso reutiliza la evaluación pura `CanView(viewerId, ownerId, visibility, areFriends)` con esos datos de la petición, sin consultar la amistad dos veces. El ámbito History ahora se aplica a `GET /api/users/{username}/library`, de forma independiente del perfil. El dueño conserva acceso; amigos y visitantes se evalúan con la misma matriz. Las reseñas publicadas en páginas de juegos mantienen su visibilidad. Ver [bibliotecas de usuarios](user-library.md).

Crear o eliminar una amistad y cambiar una preferencia se refleja en la siguiente lectura, sin renovar el JWT. La escritura modifica solo las preferencias y el `ConcurrencyStamp` de Identity: una edición de perfil que haya cargado datos viejos no puede sobrescribir la privacidad recién guardada.

## Valores iniciales y migración

`Visibility`: 0=Public, 1=Friends, 2=Private.

`ProfileSettings` agrega `DefaultProfileVisibility` (public) y `DefaultHistoryVisibility` (friends). Se validan al inicio y se aplican tanto al registro como a la creación del administrador inicial. Cambiar la configuración afecta a cuentas nuevas; no reescribe preferencias guardadas.

La migración `20261108005013_AddUserPrivacy` congela los defaults acordados para cuentas existentes: public/friends. No lee configuración mutable en tiempo de migración. Las propiedades se escriben explícitamente al insertar, de modo que `HistoryVisibility=Public` no sea reemplazado por el default Friends del servidor al tener valor numérico cero.

```sql
ALTER TABLE `Users` ADD `HistoryVisibility` int NOT NULL DEFAULT 1;
ALTER TABLE `Users` ADD `ProfileVisibility` int NOT NULL DEFAULT 0;
```

[SQL completo](../sql/20261108005013_AddUserPrivacy.sql), generado offline y **no aplicado a la base de la aplicación**. El test de migración sí ejecuta las operaciones en una base SQLite temporal aislada con usuarios anteriores para comprobar el backfill. El SQL MySQL se genera y valida offline, sin ejecutar DDL en un servidor MySQL.

## Tests y ejemplos

54 tests nuevos cubren la matriz de 18 casos del servicio para ambos ámbitos; la matriz HTTP de perfiles y listas; JSON restringido exacto; cambios inmediatos; solicitudes a perfiles privados; búsqueda de perfiles privados; valores inválidos; autenticación y aislamiento; límite compartido; cuentas sin confirmar; defaults configurados para registro y seed; backfill de existentes; escritura explícita de Public; conflictos con ediciones viejas; Swagger; y preservación de biblioteca y reseñas.

No se modificaron tests preexistentes en esta fase.

Pruebas con tres cuentas: [privacy.http](../requests/privacy.http). Guía completa de las cuatro fases: [social-profiles.md](social-profiles.md).
