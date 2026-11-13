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
