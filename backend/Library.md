# Biblioteca privada — fase 1

Implementados el modelo y `ILibraryService`; los endpoints llegan en la fase 2.
El servicio solo consulta juegos locales. No usa `GameService` ni el proveedor
externo del catálogo. No se registran horas jugadas.

## Persistencia

- Estados guardados en `varchar(16)` mediante conversión explícita:
  `Playing → playing`, `Backlog → backlog`, `Beaten → beaten`,
  `Completed → completed`. Los ordinales del enum no se guardan.
- Tipos de evento: `Added → added`, `StatusChanged → status_changed`,
  `Removed → removed`. Los estados de origen y destino usan la misma conversión,
  con `null` para el origen del alta y el destino de la baja.
- Fechas efectivas: MySQL `date`, con la conversión `DateOnly ↔ DateTime`
  que requiere Connector/NET, siguiendo `GameConfiguration`.
- Instantes reales: UTC, con recuperación explícita de `DateTimeKind.Utc` al leer.
- Cada escritura usa un único `SaveChangesAsync`, que guarda la entrada y su
  evento en la misma transacción. La concurrencia usa un `Guid` en el `WHERE`
  del cambio o la baja. Una falla descarta ambas entidades pendientes del contexto.
- El índice único `(UserId, GameId)` arbitra altas concurrentes. La recuperación
  sigue el patrón de `GameRepository`: releer la identidad persistida tras el fallo
  y propagar el error de persistencia si no existe una fila ganadora.
- El usuario es propietario de entradas y eventos (`ON DELETE CASCADE`).
  El juego usa `ON DELETE RESTRICT` en ambas tablas, incluso si solo queda historial.
  Los eventos nunca dependen de una entrada ni se eliminan al quitarla.

## Decisiones de comportamiento

- El mismo estado devuelve la entrada sin validar el rango de la fecha efectiva,
  guardar, cambiar el token ni generar un evento. La validación del formato del
  cuerpo HTTP corresponde a la fase 2.
- Un cambio reintenta una vez tras un conflicto, releyendo estado, token y fecha
  mínima. Si el ganador ya dejó el estado solicitado, el reintento es idempotente.
- La baja usa la fecha UTC actual y el estado vigente como origen. También relee
  y reintenta una vez si compite con un cambio; conflictos persistentes producen
  `concurrent_modification`.
- Volver a agregar inicia una entrada nueva. Su fecha efectiva no se compara con
  eventos de entradas anteriores; la regla de fecha mínima rige el ciclo vigente.
- `GetEntryAsync` devuelve `null` cuando ese usuario no tiene el juego.
- `LibraryException.Code` identifica los errores esperados;
  `LibraryException.MinDate` conserva la fecha mínima para la futura capa web.

## Migración y validación

El [SQL generado](sql/20261028004629_AddLibrary.sql) crea únicamente las dos tablas,
sus claves e índices y la entrada del historial de EF. **No se aplicó a la base.**
Se generó con la fábrica de diseño existente y `--offline`, sin arrancar la API,
ejecutar el seed ni conectar a MySQL.

```powershell
dotnet build backend/Playtrough.sln --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
```

Los tests de biblioteca usan SQLite temporal con conexiones independientes y WAL,
reloj controlable, carreras coordinadas e inserciones de eventos que fallan dentro
de la transacción. El mapeo del proveedor MySQL se comprueba sin abrir conexiones;
no se ejecutó la migración ni una integración contra MySQL real.
