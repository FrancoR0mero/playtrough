# Prueba manual de reseñas y puntuación

Comandos para PowerShell 7 con `curl.exe`. Reiniciá la API con el código actual,
usá una cuenta con email confirmado y obtené su access token mediante
`POST /api/auth/login` en Swagger. Los endpoints aceptan cualquier rol. Reemplazá
el marcador del token solo en tu terminal; no guardes credenciales en archivos.

La migración `20261031000047_AddReviews` ya se aplicó a la base de desarrollo.
No hay otra migración para las fases 2 y 3. En otro entorno debe aplicarse primero
la migración revisada con una cuenta que pueda crear el trigger de cascada.

## Preparación y distinción entre IDs

Elegí un ID externo disponible en tu catálogo. El detalle devuelve el `id` local
que usan las rutas de reseñas; no asumas que coincide con `externalId`.

```powershell
$reviewApiBase = 'http://localhost:5000'
$reviewAuthorization = 'Authorization: Bearer PEGAR_ACCESS_TOKEN'
$reviewExternalId = 19

$reviewGame = curl.exe --silent --show-error --fail-with-body -H $reviewAuthorization "$reviewApiBase/api/games/$reviewExternalId" | ConvertFrom-Json
$reviewGameId = $reviewGame.id
$reviewMineUrl = "$reviewApiBase/api/games/$reviewGameId/reviews/me"
$reviewListUrl = "$reviewApiBase/api/games/$reviewGameId/reviews"

curl.exe -i -H $reviewAuthorization $reviewMineUrl
curl.exe -i -H $reviewAuthorization $reviewListUrl
```

Para observar un alta 201 y contadores desde cero, elegí un juego cuyo listado tenga
`count: 0` y cuya reseña propia devuelva 404 `review_not_found`. Si ya tenés una
reseña, el primer PUT la edita y devuelve 200.

## Crear, editar y repetir la misma edición

El JSON se envía por stdin para conservar las comillas en Windows.

```powershell
# Alta: 201 y Location del GET propio.
'{"rating":4.5,"text":"Buen juego."}' | curl.exe -i -X PUT -H $reviewAuthorization -H 'Content-Type: application/json' --data-binary '@-' $reviewMineUrl

# Edición: 200; cambia EditedAt y se ajusta RatingSum por la diferencia.
'{"rating":3.5,"text":"Opinión actualizada."}' | curl.exe -i -X PUT -H $reviewAuthorization -H 'Content-Type: application/json' --data-binary '@-' $reviewMineUrl

# Repetición idéntica: 200 con la misma reseña, fechas y contadores.
'{"rating":3.5,"text":"Opinión actualizada."}' | curl.exe -i -X PUT -H $reviewAuthorization -H 'Content-Type: application/json' --data-binary '@-' $reviewMineUrl

curl.exe -i -H $reviewAuthorization $reviewMineUrl
curl.exe -i -H $reviewAuthorization "$reviewApiBase/api/games/$reviewExternalId"
```

Si era la única reseña, después del alta los contadores son `1` y `4.5`; después
de la edición y su repetición son `1` y `3.5`. El detalle debe reflejar el resumen
inmediatamente, aunque los metadatos del catálogo ya estén cargados.

Para una reseña solo con puntuación, omití `text` o mandá null. Un texto de solo
espacios también se guarda como null. Las puntuaciones válidas son 1, 1.5, 2, 2.5,
3, 3.5, 4, 4.5 y 5; en JSON se usa punto decimal.

## Listar con cada orden y paginar

```powershell
curl.exe -i -H $reviewAuthorization "${reviewListUrl}?sort=recent&page=1&pageSize=10"
curl.exe -i -H $reviewAuthorization "${reviewListUrl}?sort=top&page=1&pageSize=10"
curl.exe -i -H $reviewAuthorization "${reviewListUrl}?sort=low&page=1&pageSize=10"

curl.exe -i -H $reviewAuthorization "${reviewListUrl}?sort=recent&page=1&pageSize=2"
curl.exe -i -H $reviewAuthorization "${reviewListUrl}?sort=recent&page=2&pageSize=2"
```

`recent` usa creación descendente e ID descendente; `top` antepone puntuación
descendente y `low` puntuación ascendente. En los tres casos los empates usan
creación e ID descendentes. Para comprobar empates y varias páginas, agregá reseñas
con otras cuentas de prueba: una cuenta solo puede tener una reseña por juego.
No deberían repetirse ni faltar IDs mientras ese conjunto no cambie entre lecturas.

Verificá que cada autor tenga solo `userId` y `username`, que aparezca también tu
reseña y que `isMine` sea verdadero solo en ella. Al cambiar el token por el de
otra cuenta, cambia `isMine`, no el conjunto público de reseñas. `ratingSummary`
corresponde a todas las reseñas del juego, independientemente de la página u orden.

El tamaño predeterminado es 10 y el máximo es 30. Un valor superior produce 400
`invalid_query`; no se recorta silenciosamente. La página y su resumen comparten
una transacción. Al agregar, editar o borrar reseñas entre peticiones, la paginación
por offset puede desplazar elementos; el contrato no incluye cursor ni snapshot
compartido entre varias páginas.

## Eliminar y verificar el estado final

```powershell
curl.exe -i -X DELETE -H $reviewAuthorization $reviewMineUrl
curl.exe -i -H $reviewAuthorization $reviewMineUrl
curl.exe -i -H $reviewAuthorization $reviewListUrl
curl.exe -i -H $reviewAuthorization "$reviewApiBase/api/games/$reviewExternalId"
```

DELETE devuelve 200 con el resumen actualizado. GET propio pasa a 404
`review_not_found`. Si era la única reseña, el listado queda vacío y el promedio
es null con count 0; si quedan otras, el resumen se calcula con esas reseñas.

## Comprobar los contadores en MySQL

Ejecutá la consulta antes y después de cada paso en tu cliente de base. Sustituí
`123` por el ID local mostrado en `$reviewGameId`. `CountersMatch` debe ser 1.

```sql
SET @review_game_id = 123;

SELECT
    g.Id,
    g.RatingCount,
    g.RatingSum,
    COALESCE(r.ActualCount, 0) AS ActualCount,
    COALESCE(r.ActualSum, 0) AS ActualSum,
    CASE WHEN g.RatingCount = 0 THEN NULL
         ELSE ROUND(g.RatingSum / g.RatingCount, 1) END AS Average,
    (g.RatingCount = COALESCE(r.ActualCount, 0)
     AND g.RatingSum = COALESCE(r.ActualSum, 0)) AS CountersMatch
FROM Games AS g
LEFT JOIN (
    SELECT GameId, COUNT(*) AS ActualCount, SUM(Rating) AS ActualSum
    FROM Reviews
    GROUP BY GameId
) AS r ON r.GameId = g.Id
WHERE g.Id = @review_game_id;

SELECT Id, UserId, GameId, Rating, Text, CreatedAt, EditedAt, ConcurrencyToken
FROM Reviews
WHERE GameId = @review_game_id
ORDER BY CreatedAt DESC, Id DESC;
```

Para comprobar todos los juegos, quitá el filtro `WHERE g.Id = @review_game_id`
de la primera consulta. Todas las filas deben tener `CountersMatch = 1`.
La edición idéntica conserva `EditedAt` y `ConcurrencyToken`, además de los contadores.

## Errores y límites

```powershell
# 400 invalid_rating.
'{"rating":4.1}' | curl.exe -i -X PUT -H $reviewAuthorization -H 'Content-Type: application/json' --data-binary '@-' $reviewMineUrl

# 400 invalid_text.
'{"rating":4.5,"text":123}' | curl.exe -i -X PUT -H $reviewAuthorization -H 'Content-Type: application/json' --data-binary '@-' $reviewMineUrl

# 400 invalid_query.
curl.exe -i -H $reviewAuthorization "${reviewListUrl}?sort=other"
curl.exe -i -H $reviewAuthorization "${reviewListUrl}?pageSize=31"

# 401: requiere autenticación, aunque el contenido sea público entre usuarios.
curl.exe -i $reviewListUrl
```

PUT y DELETE comparten `reviews-write`, por defecto 20 escrituras por minuto por
usuario. Para comprobar 429 rápidamente, configurá temporalmente
`RateLimit__ReviewsWrite__PermitLimit=2` y reiniciá la API: después de dos escrituras,
la siguiente debe devolver `rate_limited` y `Retry-After`. Las lecturas usan el
límite global existente. Restaurá la configuración al terminar.

## Decisiones de contrato

- Se admiten medias estrellas por la aclaración posterior al pedido original;
  `Rating` y `RatingSum` usan decimal exacto.
- El límite de texto cuenta valores Unicode escalares: un emoji simple cuenta como
  uno; una secuencia compuesta puede contar como varios. No se codifica HTML.
- El listado es público entre usuarios autenticados y conserva el límite global
  de lectura; no exige pertenencia a la biblioteca.
- `sort` solo admite los tres valores documentados y distingue mayúsculas; los
  parámetros vacíos son inválidos. PageSize fuera de 1–30 se rechaza.
- DELETE puede devolver 409 si persiste un conflicto tras el reintento.
- Las páginas son estables sobre datos sin cambios. No se agrega cursor al contrato.
