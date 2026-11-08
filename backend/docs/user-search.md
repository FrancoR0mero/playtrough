# Épica 9 — fase 3: búsqueda de usuarios

`GET /api/users/search?q=&page=&pageSize=` requiere autenticación de cualquier rol.

- `q` es obligatorio, de 2 a 50 valores escalares Unicode después de recortar espacios exteriores.
- `page`: 1 por defecto; `pageSize`: 20 por defecto, máximo 20. Se rechazan valores fuera del rango y desplazamientos que desborden int.
- Se busca una subcadena del username normalizado con Identity, sin distinguir mayúsculas. `%`, `_` y `\` son literales; el escape de LIKE se comparte con el listado de amigos.
- No se agrega transliteración ni eliminación de acentos: se conserva el normalizador existente. La equivalencia adicional entre caracteres acentuados y no acentuados depende de la collation de la base. Las cuentas antiguas pueden tener nombres que el registro nuevo ya no permite.
- Solo aparecen cuentas con email confirmado y se excluye al usuario del token.
- Orden: coincidencia exacta, prefijo y resto; luego username normalizado e id. La paginación es determinista para un conjunto sin mutaciones; cambios entre páginas pueden desplazar elementos, como en los listados existentes por offset.
- Respuesta: `{ items: [{ username, avatarUrl, relationship }], page, pageSize, hasMore }`. No expone email ni ids internos de usuarios. `requestId` solo se incluye en relaciones pendientes.
- La página y las relaciones se obtienen en una sola sentencia SQL. Los subqueries de amistades usan la clave primaria canónica y los de solicitudes el índice único `PendingPairKey`. No hay consultas por cada resultado ni caché; las relaciones cambian al instante.
- La ruta literal `search` tiene prioridad y continúa excluida de la ruta variable de perfiles. `me` sigue funcionando normalmente.
- Errores: `400 invalid_query` (también conversión de parámetros), 401 sin token y `429 rate_limited` con `Retry-After`.
- Política `user-search`, configurable en `RateLimit:UserSearch`: 30 consultas por minuto por usuario. Cambiar IP o renovar token no reinicia el presupuesto. Otros usuarios conservan su propio límite y los perfiles siguen accesibles.

No hay cambios de modelo ni migración nueva en esta fase. No se aplicaron las migraciones pendientes de fases anteriores.

## Verificación

28 tests nuevos de servicio y HTTP: ranking, paginación sin repeticiones, comodines y acentos, límites Unicode, exclusión del dueño y de cuentas sin confirmar, los cuatro estados de relación, actualización inmediata, una sola consulta SQL, forma pública de la respuesta, roles, 400/401/429, límite configurable, Swagger y traducción con el proveedor MySQL.

Las pruebas ejecutables usan SQLite aislado. El test MySQL verifica la traducción a SQL offline; no ejecuta consultas contra un servidor MySQL.

El único test preexistente ajustado es `ProfileEndpointsTests`: la ruta `/api/users/search` sin `q` ahora debe devolver `400 invalid_query`, en lugar del 404 anterior a esta fase.

```powershell
dotnet build backend/Playtrough.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/Playtrough.sln --no-build -m:1
```

Pruebas manuales: [user-search.http](../requests/user-search.http). Para observar las relaciones, crear estados con [friends.http](../requests/friends.http) y repetir la búsqueda desde A, B y C.
