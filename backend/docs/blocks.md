# Bloqueo de usuarios — US 188

## Persistencia, servicio y endpoints

`UserBlocks` guarda BlockerId, BlockedId y CreatedAt UTC. La clave compuesta impide duplicados; el CHECK impide autobloqueos; un índice inverso soporta la consulta en ambas direcciones. Ambos participantes tienen claves foráneas. Los bloqueos son direccionales: cada usuario administra solamente los suyos.

Todos los endpoints requieren JWT, de cualquier rol, y toman al autor del token:

- `POST /api/blocks` con `{ "username": "jugador" }`: devuelve 204. Repetir conserva la fecha original. Rechaza autobloqueo con 400 `cannot_block_self`, cuerpos inválidos con 400 `invalid_query` y cuentas inexistentes o sin confirmar con 404 `user_not_found`.
- `DELETE /api/blocks/{username}`: devuelve 204, incluso si no había bloqueo o no existe la cuenta. Solo elimina el bloqueo del autor; no restaura relaciones anteriores.
- `GET /api/blocks?page=1&pageSize=20`: devuelve `{ items: [{ user: { username, avatarUrl }, blockedAt }], page, pageSize, hasMore }`. Máximo 50 por página, orden alfabético con desempate por id. Sin email ni ids internos. Los bloqueos propios de cuentas que luego pierden confirmación siguen siendo administrables.

POST/DELETE comparten `friends-write` con amistades (30 escrituras/minuto por usuario por defecto), con 429 y Retry-After. Las lecturas usan el límite global y no se almacenan en caché.

`BlockService` inserta el bloqueo, elimina la amistad y cancela todas las solicitudes pendientes del par en una transacción. No publica notificaciones ni eventos de rechazo; el historial de solicitudes se conserva. `UserPairTransaction` utiliza los mismos bloqueos de filas de usuario y el mismo orden canónico que las escrituras de amistad. Serializa pares entre procesos y conserva la estrategia de reintentos del proveedor.

Si A bloquea a B y B bloquea a A, quitar el bloqueo de A no elimina el de B. Desbloquear nunca recrea una amistad ni una solicitud.

La migración `AddUserBlocks` solo crea la tabla y sus índices; no altera usuarios ni relaciones existentes. El SQL se genera offline. Las pruebas usan SQLite aislado y verifican también el SQL del proveedor MySQL.

## Aplicación del bloqueo

Un bloqueo en cualquiera de los sentidos prevalece sobre perfil e historial públicos. `PrivacyService` evalúa el par en ambos ámbitos, sin caché; el dueño conserva acceso propio. Los GET de perfil, biblioteca y amigos ajenos devuelven 404 `user_not_found`, igual que una cuenta no disponible, sin revelar quién bloqueó a quién.

La búsqueda excluye bloqueos en ambas direcciones antes de ordenar y paginar, en la misma consulta SQL. Las listas de amigos también excluyen cuentas bloqueadas respecto del visitante, incluso al consultar la lista de un tercero. Crear un bloqueo elimina los contadores de amistad y solicitudes mediante la eliminación/cancelación real de esas relaciones.

Enviar solicitudes vuelve a comprobar el bloqueo dentro de los bloqueos de filas y devuelve 404 `user_not_found`. Aceptar, rechazar o cancelar solicitudes de un par bloqueado devuelve 404 `request_not_found`; también se impide reutilizar una aceptación histórica mientras hay bloqueo. Desbloquear deja las preferencias de privacidad vigentes y exige solicitudes nuevas para volver a ser amigos. Las reseñas publicadas en páginas de juegos y los avatares públicos mantienen su visibilidad.

`BlockEnforcementTests` cubre ambos sentidos, privacidad central, consultas de terceros, conservación de reseñas, rollback ante fallo y concurrencia con aceptación o duplicación de bloqueos. Concurrencia ejecutable usa SQLite con conexiones independientes; no equivale a probar la concurrencia del motor MySQL.

## Interfaz y comprobación final

El perfil ajeno ofrece «Bloquear usuario», también cuando está restringido por privacidad. La confirmación explica la eliminación de amistad, cancelación de solicitudes y ausencia de notificaciones. Solo se oculta el perfil y se abre la lista de bloqueados cuando POST devuelve éxito. Red, errores y 429 conservan la confirmación; no hay reenvíos automáticos.

El engranaje del perfil propio lleva a Privacidad, desde donde «Usuarios bloqueados» abre `/profile/settings/blocked`. La lista propia muestra avatar, nombre y fecha, paginación y confirmación para desbloquear. Las filas se conservan si falla la operación; después de una eliminación confirmada se vuelve a consultar la primera página para evitar saltos por paginación por offset. El desbloqueo advierte que un bloqueo en sentido contrario puede seguir vigente.

Las mutaciones publican un cambio social local para actualizar contadores, amistades y biblioteca. Las listas propias también reconsultan al recuperar foco y cada 30 segundos mientras están visibles para retirar solicitudes canceladas por un bloqueo remoto. Perfil y listas retiran datos si una lectura posterior devuelve `user_not_found`; la búsqueda vuelve a consultar al recuperar foco, mientras está visible cada 30 segundos y ante cambios locales de bloqueo. No se agregaron conexiones en tiempo real.

Prueba manual con A y B amigos y C como tercero: A bloquea a B, ambos pierden amistad y acceso mutuo a perfil/biblioteca/amigos y dejan de aparecer en búsqueda. C mantiene sus accesos según privacidad; las reseñas publicadas se conservan. B puede bloquear a A por API si conoce su username; cada uno verá únicamente sus propios bloqueos. Quitar solo uno de los dos bloqueos no restaura acceso. Quitar ambos permite nuevas solicitudes, sin recrear la amistad anterior. Verificar también bloqueo con solicitud pendiente, cancelación de las confirmaciones, errores de red, 429 y rutas directas.

La migración `20261113070456_AddUserBlocks` se aplicó y verificó en la base de desarrollo el 13 de noviembre de 2026, sin migraciones pendientes. En otros entornos: `dotnet ef database update AddUserBlocks --project backend/src/Playtrough.Api`. El SQL revisable está en `backend/sql/20261113070456_AddUserBlocks.sql`.

Validación: suite completa de backend (1108 pruebas), frontend (849 pruebas), compilaciones y lint; revisión visual en 1365 y 390 px con respuestas ficticias. Pruebas de interfaz en `blocks.spec.ts`, integración en `profile-view.spec.ts` y guards en `app.routes.spec.ts`.
