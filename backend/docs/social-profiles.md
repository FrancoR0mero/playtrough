# Épica 9 — guía final de perfiles sociales y amistades

Las cuatro fases están implementadas en backend con tests y cuentan con interfaz de perfiles, amistades, búsqueda y privacidad. La extensión de biblioteca ajena y el acceso por engranaje se describen en [bibliotecas de usuarios](user-library.md). Los endpoints requieren un JWT de cualquier rol salvo el registro, que mantiene su acceso anónimo.

## Estado de base de datos

Se generaron tres migraciones, **sin aplicarlas a la base de la aplicación**:

1. [AddUserProfiles](../sql/20261107232526_AddUserProfiles.sql): bio utf8mb4 y fecha del último cambio de username.
2. [AddFriendships](../sql/20261108000454_AddFriendships.sql): solicitudes, amistades y restricciones de unicidad.
3. [AddUserPrivacy](../sql/20261108005013_AddUserPrivacy.sql): privacidad de perfil e historial.

Revisar los SQL antes de aplicarlos. La aplicación no migra automáticamente al iniciar. Después de aprobarlos y con la conexión configurada para la base deseada, EF puede aplicar las migraciones pendientes hasta `AddUserPrivacy`:

```powershell
dotnet ef database update AddUserPrivacy --project backend/src/Playtrough.Api
```

Ese comando no se ejecutó durante esta implementación. No ejecutar los scripts y luego EF sin comprobar `__EFMigrationsHistory`. MySQL puede confirmar DDL implícitamente, aunque el script generado incluya `START TRANSACTION`.

## Decisiones principales

- Se mantuvo `MySql.EntityFrameworkCore`, el proveedor existente, en lugar de cambiar a Pomelo.
- Los usernames nuevos admiten ASCII, números y `_`; se eliminó la aceptación de puntos del registro, sin renombrar cuentas existentes. El cambio solo de capitalización es una operación sin cambios y no consume el plazo de 30 días.
- La bio y el largo de búsqueda cuentan valores escalares Unicode. Bio reutiliza el saneamiento de reseñas. El email solo se devuelve en el perfil propio.
- `CreatedAt` ya existía. Su migración histórica dejó `0001-01-01` en cuentas anteriores; se documentó sin alterar esas fechas fuera del alcance acordado.
- Una clave de par canónica generada virtual y un índice único impiden solicitudes pendientes duplicadas en ambas direcciones. Los bloqueos de filas en orden de id protegen también la cuota del remitente y las transiciones. Amistades usan clave compuesta y CHECK de orden canónico.
- Rechazar no emite eventos; su espera es direccional. Cancelar o eliminar amistad no generan espera. Reaceptar una solicitud histórica aceptada no recrea una amistad que fue eliminada después.
- Los eventos de envío y aceptación se publican después del commit, sin handlers. El publicador en memoria no promete entrega durable frente a caídas; no se agregó outbox, feed ni notificaciones.
- La búsqueda se resuelve en una sola consulta con relaciones y escape literal de comodines. Conserva acentos; las equivalencias adicionales dependen de la collation existente. La paginación por offset es estable para un conjunto de datos sin cambios entre páginas.
- Perfil restringido: campos sensibles en null dentro del servicio y **omitidos del JSON**. La búsqueda y recepción de solicitudes siguen disponibles.
- Defaults de privacidad configurables para cuentas nuevas; la migración establece public/friends para las existentes. Cambiar configuración no cambia preferencias ya guardadas.
- History se aplica mediante `PrivacyService` a la consulta de bibliotecas ajenas. Las reseñas de juegos conservan su visibilidad. Ver [bibliotecas de usuarios](user-library.md).

## Prueba manual con A, B y C

Crear tres cuentas confirmadas con nombres, por ejemplo, `gamer_a`, `gamer_b` y `gamer_c`; iniciar sesión y copiar sus access tokens en los archivos `.http`. Todos usan la dirección de desarrollo del repo, `http://localhost:5000`; ajustarla si se usa otro host.

1. [Perfiles](../requests/profiles.http): A consulta y edita su bio; B y C consultan a A sin recibir email. Cambiar el username de A, forzar refresh y conservar ambos tokens nuevos. Actualizar `usernameA` en los demás archivos si se cambió el nombre. Otro cambio efectivo inmediato debe devolver 409 con `availableAt`.
2. [Amistades](../requests/friends.http): A envía a B y B acepta; A envía a C y C rechaza; C envía a B y cancela. Copiar cada id de respuesta 201 en la variable correspondiente. Revisar listas, contadores, idempotencia y errores de permisos. Eliminar A–B y comprobar que se puede volver a enviar sin espera.
3. [Búsqueda](../requests/user-search.http): buscar desde A, B y C. Cada uno se excluye a sí mismo y ve la relación desde su perspectiva. Comprobar exacto/prefijo/resto, páginas, `_` literal, límites y ausencia de email e ids internos.
4. [Privacidad](../requests/privacy.http): dejar A–B como amigos y C como desconocido. A elige friends: B ve el perfil y la lista; C recibe perfil restringido y 403 para la lista. A elige private: B pierde acceso aunque sigan siendo amigos. C sigue encontrando a A y puede solicitar amistad. A acepta, cambia a friends y C obtiene acceso; C elimina la amistad y lo pierde inmediatamente. El dueño siempre conserva acceso.

Cada archivo contiene los endpoints y cuerpos exactos. No ejecutar en bloque las secciones de solicitudes sin actualizar sus ids ni reutilizar un refresh token ya rotado.

## Verificación automática

```powershell
dotnet build backend/Playtrough.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/Playtrough.sln --no-build -m:1
```

Los tests ejecutables usan SQLite aislado; concurrencia usa conexiones independientes. Se valida generación de migraciones y traducción de búsqueda con el proveedor MySQL offline, pero no se ejecutaron DDL ni pruebas de concurrencia contra un servidor MySQL. Docker estaba instalado pero su motor no estaba activo. Esa validación de motor real queda pendiente.

Tests preexistentes ajustados durante la épica: `SwaggerDocumentationTests` para las etiquetas Users/Friends y `ProfileEndpointsTests` para el contrato de la ruta search implementada. No se corrigieron bugs anteriores ajenos a la épica.
