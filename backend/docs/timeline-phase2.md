# Línea de tiempo: fase 2

Esta fase agrega lectura, filtros, períodos, cursores y límites por usuario. No agrega tablas ni migraciones. Los endpoints para ocultar entradas y el feed quedan para las fases siguientes.

## Endpoints autenticados

| Endpoint | Uso |
|---|---|
| `GET /api/timeline/me` | Línea propia, incluyendo las entradas ocultas. |
| `GET /api/timeline/me/periods` | Años y meses con actividad propia. |
| `GET /api/users/{username}/timeline` | Línea ajena visible según la privacidad de cuenta; omite ocultas. |
| `GET /api/users/{username}/timeline/periods` | Períodos con los mismos filtros de privacidad y ocultas. |

La consulta por username del propio dueño se comporta como `me`, incluso si su cuenta aún no está confirmada. Las rutas de usuario excluyen los literales reservados `me` y `search`. Se normaliza el username con el normalizador de Identity.

Se mantiene la privacidad única confirmada: Público permite cualquier usuario autenticado; Privado permite al dueño y sus amigos. Los valores antiguos `friends` se interpretan como privados, igual que en el servicio de privacidad vigente. Sin permiso: 403 `history_restricted`, con `restriction=private`. Usuario inexistente, ajeno sin confirmar o bloqueado en cualquiera de los sentidos: 404 `user_not_found`. Ninguna respuesta restringida incluye entradas o contadores.

## Filtros y cursor

Los listados aceptan `limit` (20 por defecto, máximo 50, ambos configurables), `year` (1970 hasta el año UTC actual + 1), `month` (1–12, requiere year) y `type` (`added`, `status_changed`, `reviewed`, combinables con comas). El orden es `OccurredOn`, `RecordedAt`, `Id`, todos descendentes. Las entradas retroactivas aparecen según la fecha efectiva.

Las respuestas tienen `{ items, nextCursor, hasMore }`. Se consultan `limit + 1` filas; `nextCursor` es null al terminar. El cursor `v1` contiene una posición completa protegida por ASP.NET Core Data Protection, codificada en Base64Url canónico sin padding. Se valida su versión, longitud, codificación, autenticidad y valores. Está ligado al dueño, visitante, visibilidad y filtros; no puede reutilizarse en otra consulta. El límite puede cambiar entre páginas. Los errores son 400 `invalid_cursor` o `invalid_filter`, sin devolver el contenido recibido.

En la primera página debe omitirse `cursor`. Un cursor vacío, modificado, con padding o parámetros conocidos repetidos se rechaza. Los tipos se normalizan por orden y sin duplicados. La paginación no ofrece una instantánea congelada: las inserciones anteriores al cursor no desplazan ni repiten las filas siguientes; cambios posteriores de fechas o visibilidad pueden cambiar los resultados. No se cachean permisos ni amistades.

Las claves de Data Protection deben persistirse y compartirse entre instancias si se despliega más de una; se usa la infraestructura existente. Los tests usan un proveedor efímero aislado por fixture.

## Datos y consultas

`TimelineQuery` concentra el filtro de usuario, ocultas, fecha, tipos y posición. Los juegos y reseñas se obtienen por joins con proyección de los campos necesarios. No hay consultas por entrada ni llamadas al catálogo externo. Se conservan los IDs locales; el externalId usa GameBrainId si existe. Los estados incluyen `abandoned`.

Las entradas de reseña muestran su puntuación y texto actuales. El extracto usa el límite configurable (240 por defecto), cuenta caracteres Unicode completos y corta en límite de palabra, reservando espacio para `…`. Si ninguna palabra completa cabe, se devuelve solo `…`. Sin texto, `excerpt` es null y se conserva la puntuación. Editar una reseña no cambia el ID ni las fechas de su entrada histórica.

Los períodos aceptan `type`, agrupan fechas efectivas en SQL y combinan los conteos diarios en años/meses descendentes. Así se evita depender de traducciones particulares de Year/Month sobre DateOnly con conversiones del proveedor Oracle. La cantidad de comandos no depende del número de entradas: un comando para listado o períodos propios, y lecturas adicionales constantes para resolver identidad y permisos de un perfil ajeno.

## Límite y verificación

`RateLimit:TimelineRead` configura `timeline-read` (60/minuto/usuario). Los cuatro endpoints comparten presupuesto; cambiar IP o token no cambia el usuario. Las solicitudes anónimas o con tokens inválidos reciben 401 y no consumen el presupuesto del usuario. Al agotarse se devuelve 429 `rate_limited` con `Retry-After`. El resto de las políticas conserva sus presupuestos.

Swagger documenta los cuatro endpoints bajo `Timeline`, sus filtros, DTOs y errores. La cobertura incluye orden y desempates, inserciones entre páginas, cursores manipulados y valores protegidos inválidos, filtros combinados, límites configurados, Unicode, reseñas editadas, matriz de privacidad y valores heredados, revocación de amistad, bloqueos, cuentas sin confirmar, períodos, cantidad de comandos SQL y traducción MySQL sin conexión.

```powershell
dotnet build backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
```

Los ejemplos de lectura están en [Timeline.http](../Timeline.http). La fase 3 agregará la escritura de visibilidad; por ahora las pruebas de ocultas preparan ese estado directamente en sus bases aisladas.
