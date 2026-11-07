# Integración de videojuegos: GameBrain

El backend .NET 10 usa `IGameDataProvider`, DTOs propios y un `GameBrainClient` registrado mediante `IHttpClientFactory`. `GameBrainMapper` es el único punto que interpreta JSON del proveedor. Angular llama únicamente al backend autenticado. Se conserva EF Core con **MySql.EntityFrameworkCore de Oracle**, el proveedor existente; no se incorpora Pomelo.

## Configuración

Desde la raíz del repositorio:

```powershell
dotnet user-secrets set "GameBrain:ApiKey" "TU_CLAVE_PRIVADA" --project backend/src/Playtrough.Api
dotnet user-secrets set "GameBrain:Enabled" "true" --project backend/src/Playtrough.Api
dotnet user-secrets set "Catalog:UseFake" "false" --project backend/src/Playtrough.Api
dotnet run --project backend/src/Playtrough.Api -- --environment Development
```

En producción usar `GameBrain__ApiKey` y `GameBrain__Enabled=true`. La clave se acepta únicamente desde user-secrets o variables de entorno: nunca desde appsettings, argumentos CLI, frontend o repositorio. El header enviado es `x-api-key`; los redirects HTTP están deshabilitados y se suprimen los loggers del HttpClient que podrían exponer solicitudes.

`GameBrain:BaseUrl` es `https://api.gamebrain.co/v1`. Los defaults son: timeout de 10 s por intento, búsqueda/sugerencias frescas por 10 minutos, metadatos por 1440 minutos y ofertas por 15 minutos. La validación limita cachés de metadatos a 24 h y ofertas a 60 minutos. `Catalog:UseFake=true` funciona solo en Development y usa el espacio de identificadores `fake`, separado del proveedor real. `Catalog:RefreshAfterDays=30` aplica únicamente a ese catálogo ficticio.

Eliminar manualmente los antiguos secretos `RawgSettings:*` del entorno que se deje de usar. La aplicación ya no los lee. No se modifican automáticamente los secretos privados del desarrollador.

## Cuota y resiliencia

El plan free permite 50 tokens diarios, 60 requests/minuto, una llamada concurrente, uso no comercial y requiere atribución. Una búsqueda cuesta un token por bloque iniciado de diez resultados; las sugerencias cuestan 0.1 token. No se precargan cinco páginas. Ver [búsqueda](https://gamebrain.co/api/docs/search-games), [sugerencias](https://gamebrain.co/api/docs/suggest-games) y [planes](https://gamebrain.co/api).

Un gate singleton limita concurrencia durante toda la lectura de respuesta y espacia inicios un segundo. Incluye reintentos: hasta tres para 429/5xx, con backoff exponencial y respeto de Retry-After. Una espera solicitada mayor a 30 segundos se trata como indisponibilidad, sin reintentar antes de tiempo. **La coordinación es por proceso**: desplegar una sola instancia con la clave free; varias instancias requieren un coordinador distribuido.

- 401/403 del proveedor: error de configuración, sin reintento; el backend responde 503 `catalog_unavailable`, sin confundirlo con el JWT del usuario.
- 402: usar la copia aún válida. Si no hay resultados de búsqueda válidos, responder 200 con `items=[]`, `isLimited=true`; la UI muestra disponibilidad limitada. En detalle se conserva la identidad y se muestra una ficha sin metadatos actualizados, manteniendo biblioteca y reseñas.
- 404: detalle inexistente o match fallido, sin reintento. El backfill registra cada miss.
- `X-API-Quota-Left`: se registra como valor numérico; no se registran claves, URLs de solicitudes ni cuerpos.

El footer y el modal incluyen `<a href="https://gamebrain.co/api">Video Games API</a>`.

## Caché y persistencia

Los IDs locales `Games.Id` y las FK de biblioteca/reseñas no cambian. `Provider`, `ExternalId` y el slug heredado se conservan para filas antiguas; `GameBrainId` nullable tiene un índice único. `SteamId`/`IgdbId` opcionales permiten importar identificadores confiables antes del backfill.

Antes del primer refresco, `LegacyMetadataJson` conserva los metadatos originales de las filas RAWG. `ProviderDetailsJson` guarda DTOs propios sin ofertas; `MetadataExpiresAt` conserva la caducidad desde la obtención original de la respuesta, incluso si vino de caché HTTP. Al expirar, se eliminan exclusivamente los metadatos de GameBrain vencidos y se restaura el respaldo anterior cuando existe; si no, queda una ficha `Juego #ID`. No se borran juegos, bibliotecas ni reseñas. `SourceUrl` es un identificador URL del proveedor.

La limpieza se ejecuta cada minuto mientras la API está activa, y también antes de servir fichas/bibliotecas. No se muestran metadatos vencidos; con el proceso detenido la limpieza física se reanuda al iniciar. Para una obligación de eliminación física puntual incluso con la app apagada se necesita programar limpieza externa. Al dejar de usar la integración, hay que eliminar las copias de metadatos GameBrain; los IDs pueden conservarse. Ver [términos](https://gamebrain.co/api/terms).

Las respuestas de búsqueda/sugerencias tienen fallback de hasta 24 h sin renovar su edad al usarlas. El detalle HTTP incluye ofertas y su caché es corta; el fallback de metadatos excluye ofertas. Actualmente no se muestran precios en la UI.

## Migraciones y backfill

Las migraciones `AddGameBrainIdentity` y `AddGameBrainDetailsCache` son aditivas. No se ejecutan automáticamente al iniciar. Revisar el [SQL](../sql/gamebrain-migration.sql), respaldar la base de práctica y aplicar explícitamente:

```powershell
dotnet ef database update --project backend/src/Playtrough.Api -- --environment Development
dotnet run --project backend/src/Playtrough.Api -- --environment Development --backfill-gamebrain --report gamebrain-backfill.jsonl
```

El comando exige proveedor real habilitado y `Catalog:UseFake=false`, y evita iniciar el servidor web, los workers o el seeder de Identity. Resuelve filas `rawg` sin `GameBrainId`, secuencialmente: Steam → IGDB → nombre+año; espera 1.1 s entre intentos. Si falta el año y no hay otro identificador, registra `missing_identifiers_or_year` en lugar de inventar una fecha. No consulta otra vez las filas ya resueltas. Los matches por nombre/año son aproximados: revisar esos éxitos además de los misses.

El reporte JSON Lines se agrega al archivo existente y se vacía a disco después de cada resultado. Una colisión se reporta sin fusionar juegos ni reseñas. Códigos de salida: 0 completado (puede haber pendientes manuales), 2 cuota agotada, 3 credenciales rechazadas, 4 proveedor no disponible. Corregir la causa y repetir; Ctrl+C permite interrumpir conservando lo guardado. Los misses pueden volver a consultarse al repetir. No ejecutar varias instancias simultáneas del backfill ni compartir la clave con una API activa durante la migración free.

Las filas antiguas ficticias que quedaron en el espacio `rawg` no se distinguen automáticamente de datos reales: revisarlas antes del backfill.

## Contrato HTTP y navegación

Todos requieren JWT y conservan la política de rate limit del catálogo:

| Endpoint | Uso |
|---|---|
| `GET /api/games/suggestions?q=...&limit=10` | Type-ahead con debounce de 400 ms; máximo 10. |
| `GET /api/games/search?q=...&page=1&pageSize=20` | Búsqueda explícita; página local traducida a offset/limit. Offset máximo 1000. |
| `GET /api/games/{externalId}` | ID de GameBrain (o fake en desarrollo). Devuelve ID local y ficha. |
| `GET /api/games/local/{gameId}` | ID local estable; funciona con juegos todavía sin backfill. |
| `GET /api/games/{externalId}/similar?limit=10` | Similares; máximo 50. |

La biblioteca abre por ID local. Las nuevas navegaciones a reseñas usan `/games/local/:gameId/reviews`. La ruta antigua `/games/:externalId/reviews` sigue funcionando con IDs del proveedor activo; enlaces RAWG externos antiguos necesitan resolución explícita, pues ambos proveedores pueden reutilizar el mismo número. No se adivina el proveedor.

## Mapeo y pendientes deliberados

- `image` → portada; `release_date` → fecha; `year` se conserva como año sin inventar mes/día; `developer` → lista de un elemento.
- `link` → `sourceUrl`, nunca se presenta como slug. Se conserva `slug` por compatibilidad con filas antiguas.
- `rating.mean × 5` → `providerRating`; `rating.count` → `providerRatingCount`; `rating.mean_critics × 100` → `criticScore`. Este último es un agregado de críticos, **no Metacritic**. Las reseñas locales continúan en `ratingSummary` y nunca se sobrescriben con valoraciones externas.
- Screenshots, tags, playtime, adult-only y ofertas se mapean con nulos. Los campos enriquecidos no agregan nuevas pantallas ni reemplazan las estrellas actuales.
- Publishers, website, parent platforms, detalle ESRB y desglose de ratings no tienen equivalente directo y no eran usados por la UI. Videos, gameplay, themes y play_modes quedan para una ampliación posterior.
- No se replica automáticamente `search_precise`/`exclude_additions`: no se asume un filtro equivalente. Revisar DLC/ediciones en búsquedas y backfill.

## Verificación

```powershell
dotnet test backend/tests/Playtrough.Tests
cd frontend
npm test -- --watch=false
npm run build
```

Los tests no llaman a la API real: mapper completo/nulos, errores, paginación, cuota/caché, persistencia y backfill usan HTTP o proveedores simulados. Prueba manual: aplicar migraciones en la base de práctica; configurar clave; escribir y abrir una sugerencia; ejecutar búsqueda completa/paginación; abrir biblioteca y reseñas antes/después del backfill; revisar reportes de matches; comprobar atribución; probar cuotas y expiración mediante mocks, evitando agotar deliberadamente los tokens reales.
