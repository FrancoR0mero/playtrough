# Biblioteca de otros usuarios y configuración de privacidad

`GET /api/users/{username}/library` requiere autenticación. Reutiliza el contrato de biblioteca propia: items con estado de cada juego, contadores globales, page, pageSize y hasMore. Admite status, q, sort, page y pageSize con las mismas validaciones que `/api/library`.

El perfil resuelve una cuenta confirmada (excepto el dueño) y consulta `IPrivacyService` con `PrivacyScope.History` antes de leer juegos o contadores. Se aplica una única privacidad a toda la cuenta: `public` permite usuarios autenticados y `private` permite únicamente al dueño y sus amigos. Perfil, amigos, biblioteca, historial y reseñas comparten esta regla. Los bloqueos se aplican en ambos sentidos. Una denegación devuelve 403 `history_restricted`, sin juegos ni contadores. Inexistentes y cuentas no confirmadas ajenas devuelven 404 `user_not_found`; filtros inválidos, 400 `invalid_query`. Las respuestas no se almacenan en caché.

La interfaz ofrece «Ver biblioteca» en perfiles ajenos y «Ver mi biblioteca» en el propio. La ruta `/profile/:username/library` reutiliza la pantalla de biblioteca en modo de consulta, con filtros, estados y paginación. No ofrece gestión de la biblioteca ajena. El detalle de un juego sigue permitiendo gestionar únicamente la biblioteca del visitante autenticado. Las operaciones de escritura siguen tomando al dueño del JWT.

Se reconsulta al recuperar el foco, volver a una pestaña visible, cada 30 segundos mientras es visible y tras cambios de amistad locales. Si una consulta falla, se borran juegos y contadores ajenos para evitar conservar contenido al perder permisos. Navegar entre usuarios cancela la consulta anterior.

El engranaje del perfil propio abre `/profile/settings/privacy`; el formulario existente deja de mostrarse dentro del perfil. Se conservan GET/PUT `/api/users/me/privacy` y sus validaciones. GET y PUT usan un único campo `visibility`, con valores `public` o `private`. Las reseñas de cuentas privadas solo aparecen para su autor y sus amigos; se filtran antes de paginar y calcular `hasMore`. Los promedios del juego siguen siendo agregados globales sin datos de autor.

No requiere una migración nueva. Para compatibilidad se conservan las dos columnas existentes, pero toda actualización escribe el mismo valor en ambas. Las cuentas antiguas se interpretan como privadas si cualquiera de las columnas estaba restringida (incluido el antiguo valor `friends`); solo dos valores públicos producen una cuenta pública. Las cuentas nuevas usan un único default `Profile:DefaultVisibility` (Public de forma predeterminada). Deben existir las migraciones de perfiles, amistades y privacidad previas.

## Comprobación

Usar tres cuentas A, B y C, con A y B amigos. Agregar juegos de distintos estados a A y establecer su cuenta privada para comprobar la misma restricción en perfil, amigos, biblioteca, historial y reseñas. Alternar la cuenta entre public/private y consultar desde ambas cuentas la ruta de biblioteca, filtros y páginas. Conservar siempre el acceso propio. Eliminar la amistad A–B y comprobar que B pierde acceso al contenido de la cuenta privada en la siguiente consulta. Verificar que una biblioteca vacía autorizada se distingue de una denegada y que no se muestran juegos previos al cambiar de usuario.

Cobertura HTTP en `UserLibraryPrivacyTests`, pruebas del modo de consulta en `library.spec.ts` y navegación del engranaje en `app.routes.spec.ts`.
