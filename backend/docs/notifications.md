# Infraestructura de notificaciones

`/hubs/notifications` usa el JWT y el identificador de usuario obtenido de `sub`. SignalR mantiene las conexiones de cada cuenta y las retira al desconectarse. El hub no expone invocaciones al cliente. El transporte se cierra al expirar el token. El cliente Angular usa un token vigente en cada intento y solicita su renovación cuando hace falta. En WebSocket se acepta el token en la URL del hub, por lo que el proxy también debe omitir esa consulta de sus registros.

Los productores guardan el evento en `OutboxMessages` dentro de la misma transacción que el cambio de negocio. Cada tipo tiene un handler registrado y el worker solo reclama tipos conocidos. El lease dura un minuto. Los fallos se reintentan hasta ocho veces y el resultado queda en `ProcessedAt` o `FailedAt`.

El handler guarda la notificación desde un evento ya confirmado. La clave de deduplicación es destinatario, ID del evento y tipo de aviso. Si hay varios destinatarios, el handler persiste cada aviso por separado y un reintento recupera las filas existentes. La futura entrega por SignalR debe ocurrir después de guardar el aviso y antes de devolver el control al worker, para que este pueda reintentar un fallo de entrega. El almacenamiento conserva referencias, fecha y versión del formato.

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
