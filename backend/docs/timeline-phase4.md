# Línea de tiempo: fase 4 y cierre de la épica

`GET /api/feed?cursor=&limit=` devuelve la actividad visible de amigos, autenticada y sin tiempo real. Reutiliza `TimelineSettings` (20 por defecto, máximo 50), los cursores protegidos v1 y el presupuesto `timeline-read` de 60 lecturas por minuto por usuario. No hay cambios de esquema ni frontend.

## Reglas y decisiones confirmadas

- El feed incluye únicamente amigos confirmados, excluye al visitante, las entradas ocultas, las que tienen `FeedEligible=false` y los bloqueos en ambos sentidos.
- Se respeta la privacidad única acordada: Público y Privado permiten amigos. Por eso una cuenta privada amiga también aparece en el feed. El antiguo criterio del texto inicial que excluía todo historial privado quedó reemplazado por esa decisión.
- La elegibilidad usa el valor guardado al crear la entrada, con umbral inclusivo de `Feed:MaxBackdateDays` (7 de forma predeterminada). No se recalcula al leer o cambiar la fecha actual. Una entrada retroactiva puede aparecer en la línea de su dueño y no en el feed.
- El orden y la posición del cursor usan `RecordedAt` e `Id`, descendentes. La fecha efectiva se muestra, pero no ordena el feed. Las altas, cambios de estado y reseñas conservan el mismo ID de timeline; bajas y ediciones no generan otra entrada.
- La respuesta es `{ items, nextCursor, hasMore }`. Cada item tiene los campos de TimelineEntryDto y `author: { username, avatarUrl }`, en el mismo nivel. La reseña y los datos del autor/juego se leen en su estado actual. `isHidden` siempre es false.
- El cursor usa el mismo sobre v1 protegido, con contexto `feed:{viewerId}` y día centinela cero. Solo el registro y el ID definen la posición. No es intercambiable con un cursor de timeline ni con otro visitante; se rechazan versiones distintas, modificaciones, padding y valores inválidos con 400 `invalid_cursor`. Límites inválidos usan 400 `invalid_filter`.
- Las amistades, confirmaciones y bloqueos se consultan de nuevo en cada petición. Eliminar una amistad revoca su actividad en la siguiente lectura; crearla permite su actividad elegible. La paginación usa keyset, sin congelar una instantánea histórica.

Se mantienen otras decisiones ya documentadas: proveedor MySQL de Oracle, estado `abandoned`, extractos Unicode de hasta 240 caracteres con límite de palabra y `…` incluido, reseña sin texto con excerpt null, y privacidad única que incluye la biblioteca y las reseñas. Los promedios de juegos siguen siendo agregados globales sin datos de autor.

## Consulta y EXPLAIN real

`TimelineQuery` construye el feed con un UNION ALL de las dos direcciones del par canónico de amistad, joins a usuario/juego/reseña y el filtro central de ocultas. El par `UserLowId < UserHighId` evita amigos duplicados y garantiza que el visitante no sea su propio amigo. La consulta incluye `limit + 1` y no realiza consultas por amigo ni por entrada. Las pruebas con interceptores verifican un único comando aunque aumente la actividad y el número de amigos.

Se ejecutaron SELECT de primera página y continuación y `EXPLAIN FORMAT=JSON` en MySQL **8.4.13** local, sin modificar datos. La evidencia está en [feed-explain.json](feed-explain.json) y el SQL reproducible en [feed-explain.sql](../sql/feed-explain.sql).

El plan obtenido usa:

| Recurso | Índice elegido | Acceso |
|---|---|---|
| TimelineEntries | IX_TimelineEntries_FeedEligible_UserId_RecordedAt_Id | ref |
| Amistades con visitante en UserLowId | PRIMARY | ref, índice cubriente |
| Amistades con visitante en UserHighId | IX_Friendships_UserHighId | ref, índice cubriente |
| Users, Games y Reviews | PRIMARY | eq_ref |
| UserBlocks | IX_UserBlocks_BlockedId_BlockerId | índice y anti-join |

La elegibilidad se expresa como igualdad parametrizada. En el primer plan, el predicado booleano simple se traducía a una comparación con cero y MySQL elegía el índice de usuario/fecha efectiva; la igualdad permite el acceso por el primer componente del índice específico del feed. Se agregó una prueba de traducción SQL para proteger esa forma.

MySQL también muestra materialización del conjunto de amigos y `using_filesort` para el orden global por registro: el índice está segmentado por usuario y no ordena conjuntamente a todos los amigos. El plan es de una base pequeña (**0 entradas de timeline y 2 amistades**); confirma los accesos e índices elegidos, pero no constituye una medición de rendimiento a gran escala. No se añadieron índices ni hints sin evidencia de necesidad.

## Validación y prueba manual con tres usuarios

La cobertura incluye amigos en ambas direcciones, exclusión de actividad propia/no amiga/oculta/no elegible/no confirmada/bloqueada, cuenta amiga privada, orden por registro y empates, elegibilidad persistida, paginación con inserciones, cursores inválidos y de otros contextos, cambio de amistades/visibilidad, datos actuales, una sola consulta, feed vacío, 401, 429 y Swagger.

Resultado: **build sin errores ni advertencias y 1271 pruebas aprobadas**, sin fallos ni omisiones (22 casos nuevos de fase 4). El único test previo ajustado en esta fase es `SwaggerDocumentationTests`: reconoce la nueva etiqueta Feed y su ruta. Las comprobaciones de API usan bases aisladas; el recorrido manual A/B/C queda preparado para ejecutarlo con cuentas locales y sus tokens, sin generar actividad en cuentas existentes durante la validación.

```powershell
dotnet build backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
```

Usar tres cuentas de prueba confirmadas: A (Admin, para ejecutar el backfill), B y C. Obtener sus JWT por login y completar las variables de [Timeline-three-users.http](../Timeline-three-users.http). A y B serán amigos; C será desconocido. Elegir dos juegos locales que esas cuentas todavía no tengan en su biblioteca, o usar el cambio de estado para juegos ya agregados.

1. Con A, ejecutar el backfill y repetirlo: la segunda ejecución devuelve cero creaciones si no hubo nuevas fuentes faltantes. No aplica migraciones.
2. A envía una solicitud a B; B la acepta usando el requestId de la respuesta.
3. B agrega un juego, cambia su estado y crea una reseña. Su línea muestra tres entradas; editar la reseña cambia el contenido de la misma entrada. A consulta el feed y obtiene actividad de B; C sigue sin verla en su feed.
4. A agrega otro juego con una fecha efectiva de más de 7 días atrás. Lo ve en su línea, pero B no obtiene esa entrada en el feed. Para comparar órdenes, crear también actividad reciente: la línea ordena por fecha efectiva y el feed por registro.
5. A configura Público: B y C pueden consultar su línea y períodos. A configura Privado: B sigue pudiendo verlos y C recibe 403 history_restricted con restriction=private. A siempre conserva su vista propia.
6. B obtiene un entryId de su línea y lo oculta. A deja de verlo tanto en el feed como en la línea/períodos de B; B sigue viéndolo marcado. A intenta ocultar esa entrada ajena y recibe 404 entry_not_found. B vuelve a mostrarla y se restituye.
7. Consultar filtros combinados de año/mes/tipos y períodos por tipo. Copiar nextCursor sin modificarlo y mantener la misma consulta al paginar. Los cursores de feed y timeline son diferentes.
8. A elimina la amistad con B: la actividad de B desaparece del feed en la siguiente petición y el acceso a una cuenta privada se revoca. Volver a enviar y aceptar una solicitud permite verla de nuevo.

Las fases 1–4 completan el backend de la épica. Me gusta, comentarios, reportes, notificaciones, tiempo real, agrupación y feed global siguen fuera de alcance.
