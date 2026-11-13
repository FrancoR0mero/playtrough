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
