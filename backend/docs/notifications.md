# Infraestructura de notificaciones

`/hubs/notifications` usa el JWT y el identificador de usuario obtenido de `sub`. SignalR mantiene las conexiones de cada cuenta y las retira al desconectarse. El hub no expone invocaciones al cliente. El transporte se cierra al expirar el token. El cliente Angular usa un token vigente en cada intento y solicita su renovación cuando hace falta. En WebSocket se acepta el token en la URL del hub, por lo que el proxy también debe omitir esa consulta de sus registros.

Los productores guardan el evento en `OutboxMessages` dentro de la misma transacción que el cambio de negocio. Cada tipo tiene un handler registrado y el worker solo reclama tipos conocidos. El lease dura un minuto. Los fallos se reintentan hasta ocho veces y el resultado queda en `ProcessedAt` o `FailedAt`.

El handler guarda la notificación desde un evento ya confirmado. La clave de deduplicación es destinatario, ID del evento y tipo de aviso. Si hay varios destinatarios, el handler persiste cada aviso por separado y un reintento recupera las filas existentes. `NotificationDelivery.PublishAsync` guarda el aviso y emite `NotificationAvailable` al destinatario con el ID como texto. Si falla la entrega, el worker reintenta con el mismo ID. El almacenamiento conserva referencias, fecha y versión del formato.

### Centro y lectura

- `GET /api/notifications` devuelve la lista más reciente primero, con `nextCursor` y `readCutoff` en la primera página cuando corresponde. El listado revalida acceso y bloqueos antes de devolver un detalle con enlace.
- `GET /api/notifications/unread-count` devuelve el contador de la sesión actual. Incluye avisos que se muestran sin detalles.
- `PUT /api/notifications/{id}/read` marca un aviso propio. Repetir la operación devuelve 204 y preserva la fecha de lectura.
- `POST /api/notifications/read-all` recibe `{ "cutoff": "<readCutoff>" }` y devuelve el contador. El corte protegido está vinculado a la cuenta y fija fecha, ID y el máximo ID observado. El listado y la lectura masiva usan tokens distintos.

SignalR emite `NotificationsRead` a las sesiones de la cuenta cuando cambia una lectura. Angular vuelve a consultar la API al iniciar, al restablecer la conexión, al recuperar el foco y al recibir un aviso.

### Preferencias

`GET /api/notifications/preferences` devuelve las categorías con `enabled` y `editable`. Las categorías `friends`, `interactions` y `groups` están activas por defecto. Las categorías esenciales `account` y `moderation` se muestran activas y son de solo lectura.

`PUT /api/notifications/preferences` guarda una categoría editable de la cuenta autenticada. El cuerpo es `{ "category": "groups", "enabled": false }`. Repetir el mismo cambio conserva una única fila por usuario y categoría.

Cada consumidor indica la categoría al llamar a `NotificationDelivery.PublishAsync`. La entrega consulta la preferencia vigente antes de guardar o emitir el aviso. Un cambio de preferencia conserva los avisos que ya estaban en el centro.

### Actividad social

Las solicitudes y aceptaciones de amistad avisan a la otra cuenta. Los likes y comentarios en publicaciones avisan al autor. El propietario recibe solicitudes de ingreso y el solicitante recibe la decisión. Al quitar una membresía, el aviso se dirige a la cuenta afectada.

Los eventos se guardan con el cambio de negocio y referencian IDs sin guardar extractos de comentarios. Repetir una operación idempotente conserva un solo evento. Quitar un like y volver a darlo genera un evento nuevo. Los avisos excluyen acciones propias y evalúan la preferencia y los bloqueos vigentes. Las reacciones de reseñas y de comentarios quedan fuera de este tipo de aviso.

El centro vuelve a evaluar el acceso al listar. Una solicitud de amistad ya resuelta, un comentario eliminado o una publicación inaccesible conservan un aviso genérico sin enlace. El rechazo y la retirada de membresía se muestran sin un enlace al grupo. El worker puede procesar eventos de grupos que ya estaban pendientes si todavía corresponden al estado actual.

### Cuenta y moderación

El worker consume `ModerationDecisionApplied`, `ModerationAppealResolved`, `SubscriptionActivated`, `PaymentFailed` y `SubscriptionExpired`. Cada evento guarda solo `recipientId` en su payload. El productor toma ese valor del titular afectado y registra el evento una sola vez por transición confirmada, dentro de su transacción. `AggregateId` puede identificar la transición internamente. Los reintentos del consumidor conservan el ID de outbox y reutilizan el mismo aviso.

Los avisos esenciales se muestran en el centro del titular aunque estén desactivadas todas las categorías sociales. El centro devuelve textos fijos sin motivos de moderación ni detalles de pagos. Las pruebas encolan los eventos con productores simulados y verifican la entrega por SignalR, el listado y la deduplicación. Los productores reales y los enlaces a las pantallas personales de moderación y suscripción se activarán al incorporar esos módulos.

### Prueba local

Desde la raíz del repositorio, con el MySQL de desarrollo disponible, aplicá las migraciones e iniciá el backend y Angular en terminales separadas.

```powershell
dotnet ef database update --project backend/src/Playtrough.Api -- --environment Development
dotnet run --project backend/src/Playtrough.Api --launch-profile http
npm --prefix frontend start
```

1. Abrí la app con dos cuentas en perfiles de navegador distintos. Enviá una solicitud de amistad de A a B y verificá que la campana de B se actualice. Abrí el aviso y comprobá que lleve a las solicitudes recibidas.
2. Marcá el aviso como leído. Con el JWT de B, consultá `GET /api/notifications` y `GET /api/notifications/unread-count` en la API local. El listado debe mostrar la fecha de lectura y el contador debe reflejar el cambio. Un JWT de A recibe `404` al intentar marcar como leído el aviso de B.
3. Desactivá Interacciones para B. Desde A, comentá una publicación visible de B y verificá que el comentario aparezca sin aumentar el contador de B.
4. Para comprobar los avisos esenciales con el productor simulado, ejecutá `dotnet test backend/tests/Playtrough.Tests --filter FullyQualifiedName~EssentialNotificationTests`.

### Operación

Esta fase conserva las notificaciones y las filas de outbox. Los eventos pendientes sin handler y los fallidos quedan disponibles para análisis o recuperación. El worker registra tipo, ID e intento sin incluir el contenido del evento. La entrega en vivo usa las conexiones de la instancia actual de la API. Un despliegue con varias instancias requerirá un backplane de SignalR para enviar avisos a las conexiones registradas en otras instancias.

Tras corregir la causa de un fallo, un operador puede reencolar una fila fallida por ID y tipo. Primero debe comprobar que la versión en ejecución registra un handler para ese tipo. Conservar el ID permite deduplicar una notificación que ya se hubiera guardado.

```sql
UPDATE OutboxMessages
SET FailedAt = NULL, Attempts = 0, NextAttemptAt = NULL,
    LeaseId = NULL, LeaseExpiresAt = NULL
WHERE Id = @eventId AND Type = @eventType
    AND FailedAt IS NOT NULL AND ProcessedAt IS NULL AND LeaseId IS NULL;
```
