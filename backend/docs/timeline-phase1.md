# Línea de tiempo: fase 1

Esta fase agrega únicamente el modelo, las escrituras transaccionales y la carga histórica administrativa. Los listados, filtros, períodos, ocultación y feed quedan para las fases siguientes. No hay cambios en el frontend.

## Esquema y SQL aprobado

Migración: `20261115031802_AddTimelineEntries`. El SQL completo está en [timeline-phase1.sql](../sql/timeline-phase1.sql). Es aditiva: crea `TimelineEntries`, sus claves e índices y registra la migración. No modifica las tablas existentes ni consulta el catálogo. **El script se generó sin conexión y la migración se aplicó, tras aprobación, el 15/11/2026 a la base local `playtrought_dev` (MySQL en el puerto 3307).** MySQL realiza commits implícitos para DDL: el `START TRANSACTION` generado por EF no convierte la migración en DDL atómico.

`Id` es bigint estable. Los dos índices únicos permiten múltiples NULL y evitan repetir un evento o una reseña. Las referencias a juegos restringen su eliminación; las de usuario y orígenes se eliminan en cascada, de modo que borrar una cuenta no queda bloqueado por su timeline. La cascada de reseñas es además una protección para borrados externos al servicio.

## Escrituras

`TimelineWriter` agrega entidades al mismo `AppDbContext` y nunca llama a `SaveChanges`. `LibraryService` sigue orquestando las operaciones y `LibraryRepository` agrega la entrada antes de su guardado único, junto con la entrada de biblioteca y el evento. Los cambios idempotentes y las bajas no generan timeline; quitar y volver a agregar un juego conserva las entradas anteriores.

`ReviewService` agrega la entrada únicamente al crear la reseña. Editarla conserva su ID de timeline y sus fechas; las fases de lectura consultarán el contenido actual. Borrar una reseña elimina explícitamente su entrada dentro de la misma transacción que reseña y contadores. Los reintentos y fallos descartan también las entidades de timeline rastreadas.

La fecha efectiva y la de registro provienen del evento original o de `Review.CreatedAt` en UTC. `FeedEligible` se calcula una sola vez comparando días de calendario, con umbral inclusivo; las reseñas son siempre elegibles. Incluye el estado existente `abandoned`.

Configuración compartida en `appsettings.json` y su ejemplo:

```json
"Timeline": { "DefaultLimit": 20, "MaxLimit": 50, "ExcerptLength": 240 },
"Feed": { "MaxBackdateDays": 7 }
```

Los límites admiten 1–50 y DefaultLimit no puede superar MaxLimit. ExcerptLength admite 1–2000 y MaxBackdateDays 0–36500. La configuración inválida se rechaza al iniciar. La construcción directa antigua del repositorio conserva los defaults; la inyección de dependencias usa el escritor configurado.

## Backfill

`POST /api/admin/timeline/backfill` requiere token Admin. Otros roles reciben 403; sin token, 401. Usa el límite global existente y responde:

```json
{ "createdFromLibrary": 0, "createdFromReviews": 0 }
```

Busca solo eventos Added/StatusChanged y reseñas sin entrada, ordenados por ID, en lotes de hasta 500. Cada lote se guarda en una transacción; si se interrumpe, los lotes confirmados permanecen y se puede repetir. Los índices únicos protegen también frente a dos cargas simultáneas; una colisión comprobada vuelve a seleccionar las fuentes faltantes. Las entradas existentes, sus IDs y su visibilidad no cambian. El log del servicio registra únicamente los conteos de creación.

Con la migración aprobada y aplicada, ejecutar y repetir `POST /api/admin/timeline/backfill` con un token Admin. Los listados todavía no están implementados en esta fase.

## Verificación

```powershell
dotnet build backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
```

Las pruebas nuevas cubren esquema MySQL sin conexión, tipos de fecha, índices, settings, escritura atómica, fechas retroactivas, límite de elegibilidad, reintentos, concurrencia, cascadas y backfill de más de 500 fuentes por tipo. Las bases SQLite son aisladas y los relojes controlados. Se ajustaron únicamente las construcciones de servicios en `LibraryTestContext` y `ReviewTestContext` para inyectar el escritor; no se cambiaron expectativas de pruebas previas.

Se mantienen las decisiones confirmadas para las siguientes fases: privacidad única Público/Privado (Privado permite dueño y amigos), bloqueos en ambos sentidos y proveedor MySQL de Oracle. Esta fase no implementa otra política de privacidad.

La regresión de arranque JWT se reproduce con bytes cuya representación Base64 contiene palabras reservadas por el validador. `LoginApiFactory` usa ahora hexadecimal para conservar los 64 bytes aleatorios sin esas colisiones; la validación de producción se mantiene. `JwtStartupRegressionTests` cubre tres casos deterministas y el arranque concurrente de doce hosts. Se verificó con dos corridas completas paralelas: 1161 pruebas aprobadas en cada una.
