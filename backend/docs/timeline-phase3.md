# Línea de tiempo: fase 3

Esta fase agrega la escritura de visibilidad de entradas propias. Usa `IsHidden`, ya creada en la migración de la fase 1: no necesita una nueva migración. El feed sigue pendiente para la fase 4; no hay cambios en el frontend.

## Endpoint y permisos

`PUT /api/timeline/entries/{id}/visibility`, con token autenticado y cuerpo JSON obligatorio:

```json
{ "hidden": true }
```

`false` vuelve a mostrar la entrada. Devuelve 200 con `TimelineEntryDto` y el contenido actual del juego y de la reseña, si corresponde. Solo el dueño puede escribir, independientemente del rol del visitante, su amistad o la privacidad pública de la cuenta. IDs inexistentes o ajenos devuelven el mismo 404 `entry_not_found`. No admite un ID de autor: el dueño siempre se obtiene del token.

Cuerpo ausente, malformado, null, o propiedad hidden ausente o no booleana: 400 `invalid_body`. Sin token válido: 401. Swagger documenta el cuerpo como una propiedad booleana obligatoria, el DTO de respuesta y los errores bajo `Timeline`.

## Persistencia y lectura

La escritura actualiza únicamente `IsHidden`, con `Id` y `UserId` en el predicado SQL. Dentro de la misma transacción vuelve a proyectar la entrada por su ID exacto, incluyendo entradas antiguas fuera de las páginas cargadas. No se interpreta un conteo de filas modificadas igual a cero como ausencia: MySQL puede devolver cero en escrituras idempotentes según su configuración. Una entrada propia que ya tiene ese valor sigue devolviendo 200.

El cambio conserva el ID, fechas, elegibilidad del feed y referencias de origen. No modifica biblioteca, eventos, reseñas ni sus puntuaciones. Ocultar la entrada de una reseña no oculta ni elimina la reseña de la página del juego; esa reseña sigue respetando la privacidad de la cuenta.

El único filtro que excluye ocultas permanece en `TimelineQuery.ApplyFilters`. El dueño continúa viéndolas con `isHidden=true`; los demás dejan de verlas tanto en la línea como en los períodos. Mostrarla otra vez restaura ambos resultados. La fase 4 reutilizará el filtro central para el feed.

## Rate limiting

`RateLimit:TimelineWrite` configura `timeline-write`: 30 escrituras por minuto por usuario de forma predeterminada. Repetir el mismo valor también consume presupuesto. El presupuesto permanece igual al renovar el token o cambiar de IP, es independiente del de lectura y no se comparte entre usuarios. Anónimos y tokens inválidos reciben 401 y no consumen un presupuesto autenticado. Agotado: 429 `rate_limited` con `Retry-After`.

## Verificación

Las pruebas usan las bases aisladas existentes y cubren autorización para todos los roles, amistad sin permiso de escritura, IDs ajenos/inexistentes, cuerpo inválido, idempotencia, ocultar y mostrar, períodos, preservación de reseñas, entradas fuera de las primeras 50 filas, límites independientes, renovación de token/IP, Swagger y configuración inválida. No se cambian las expectativas de pruebas previas.

```powershell
dotnet build backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
```

Los ejemplos de ocultar y mostrar están en [Timeline.http](../Timeline.http).
