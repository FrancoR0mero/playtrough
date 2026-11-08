# Épica 9 — fase 2: amistades

Implementa solicitudes, aceptación, rechazo, cancelación, listado, eliminación y contadores. No aplica privacidad todavía ni crea notificaciones, feed o consumidores de eventos.

## Decisiones de persistencia y concurrencia

- Se conserva `MySql.EntityFrameworkCore`, el proveedor instalado (no Pomelo).
- `FriendRequests.Status` usa 0=Pending, 1=Accepted, 2=Rejected y 3=Canceled. Se conserva el historial.
- `PendingPairKey` es una columna generada **virtual**, nullable, con índice único. Para una solicitud pendiente vale `min(SenderId, ReceiverId) * 4294967296 + max(SenderId, ReceiverId)`; para los demás estados vale NULL. Los ids int positivos caben sin colisiones en bigint con signo. Esto permite historial ilimitado y una sola pendiente por par, independientemente de la dirección.
- MySQL/InnoDB admite índices únicos sobre columnas virtuales: [documentación oficial](https://dev.mysql.com/doc/refman/8.4/en/create-table-secondary-indexes.html). La variante virtual coincide con el SQL que emite el proveedor instalado; no se depende de índices parciales.
- `Friendships` tiene clave primaria `(UserLowId, UserHighId)` y CHECK `UserLowId < UserHighId`. La clave primaria ya indexa al usuario menor; hay un índice adicional para el mayor. Las solicitudes tienen índices por participante, estado, fecha e id.
- Cada escritura inicia una transacción y toma bloqueos en las filas de ambos usuarios mediante UPDATE sin cambio de `AccessFailedCount`, siempre en orden creciente de id y antes de las lecturas transaccionales. Son bloqueos de base de datos, efectivos entre procesos. También serializan la cuota de un remitente que escribe hacia distintos destinatarios. No cambian datos de Identity.
- Aceptar actualiza la solicitud e inserta la amistad dentro de la misma transacción. Si falla la inserción, la solicitud vuelve a pendiente por rollback. Dos aceptaciones obtienen el mismo resultado y una sola publicación. Ante un conflicto de clave, se relee el estado confirmado antes de devolver éxito o un error de dominio; no se silencian fallos ajenos a una operación ganadora comprobable.
- La unidad completa usa la estrategia de ejecución de EF. Los eventos se publican fuera de ella y después del commit, incluso si el cliente cancela su petición después del commit.

## Semántica

- `Friends:MaxPendingSent`: 50; `Friends:RejectCooldownDays`: 7. Opciones validadas al inicio; el cooldown puede configurarse en cero.
- Rechazar es silencioso y aplica espera solamente al remitente original, desde el último rechazo en ese sentido. Repetir el rechazo no cambia su fecha.
- Cancelar y eliminar amistad no crean espera. Repetir cancelación o eliminación es idempotente. Eliminar una cuenta inexistente o no confirmada devuelve `404 user_not_found`; eliminar una relación inexistente con una cuenta visible devuelve 204.
- Aceptar nuevamente una solicitud aceptada devuelve la fecha original, incluso si la amistad se eliminó después; no recrea la relación. Para volver a ser amigos se envía y acepta una solicitud nueva.
- Responder o cancelar una solicitud ajena devuelve `404 request_not_found`, igual que un id inexistente. Los controladores no acceden a DbContext y siempre toman el autor del JWT.
- Campos de tipos incorrectos y paginación inválida usan `400 invalid_query`. Los listados reciben `page=1` y `pageSize=20`, con máximo 50; se rechazan valores fuera del rango y desbordamientos.
- Amigos: orden por username normalizado e id; solicitudes: fecha e id descendentes. La búsqueda `q` es una subcadena literal, sin distinguir mayúsculas, con comodines escapados. Como paginación por offset, la estabilidad se garantiza para el mismo conjunto de datos; mutaciones entre páginas pueden desplazar elementos.
- Los listados no exponen email ni ids de usuarios y muestran cuentas confirmadas. Los contadores describen las relaciones y solicitudes pendientes almacenadas. No se publica el id de usuario en DTOs de amistad.
- Los perfiles ahora leen contadores reales y `relationship` (`none`, `friends`, `request_sent`, `request_received`). La consulta de relación usa una sola sentencia con búsquedas indexadas. No hay caché de amistades.
- `friends-write`: 30 por minuto por usuario, configurable mediante `RateLimit:FriendsWrite`, compartido por todos los POST y DELETE de Friends. Respuestas 429 con `code=rate_limited` y `Retry-After`.

## Eventos

`IDomainEventPublisher` publica `FriendRequestSent` y `FriendRequestAccepted` después del commit. `InMemoryDomainEventPublisher` no tiene handlers ni acumula un log: no hace trabajo al no haber suscriptores. Rechazo, cancelación y eliminación no publican eventos.

Esta interfaz no garantiza entrega durable frente a una caída del proceso después del commit. Notificaciones y feed podrán añadir suscriptores; una garantía de entrega durable requerirá un outbox, fuera de esta fase.

## Migración y validación

Migración `20261108000454_AddFriendships`, posterior a `20261107232526_AddUserProfiles`. Generada offline, **no aplicada**. El SQL completo está en [backend/sql/20261108000454_AddFriendships.sql](../sql/20261108000454_AddFriendships.sql). Crea únicamente `FriendRequests`, `Friendships` e índices y registra la versión de migración. Las migraciones de perfiles y amistades continúan pendientes de aplicación en la base real.

Los tests usan bases SQLite temporales aisladas, conexiones independientes para concurrencia, reloj controlado y un publicador espía que comprueba desde otra conexión que el commit ya es visible. Cubren permisos, transiciones, idempotencia, espera, límite de 50 y configurable, rollback por fallo simulado, envíos y aceptaciones simultáneas, cuota entre pares distintos, restricciones directas, listados, perfiles, autenticación, Swagger y rate limiting.

El test MySQL genera y comprueba el SQL con el proveedor real y detecta cambios pendientes del snapshot, pero no ejecuta DDL ni concurrencia en un servidor MySQL: Docker está instalado y su motor no está activo en este entorno. Esto sigue siendo una limitación de la validación; las pruebas de concurrencia ejecutadas son sobre SQLite.

```powershell
dotnet build backend/Playtrough.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/Playtrough.sln --no-build -m:1
```

El único test preexistente ajustado en esta fase es `SwaggerDocumentationTests`, para agregar la etiqueta y las rutas Friends. Los ejemplos de tres usuarios están en [friends.http](../requests/friends.http).
