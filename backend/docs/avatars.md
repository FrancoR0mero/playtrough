# Avatar de usuario — US 79

## Subtareas implementadas

- **Almacenamiento:** `IAvatarStorage` y `LocalAvatarStorage` guardan únicamente WebP procesados, con claves GUID generadas por el servidor. `Avatar:StoragePath` es configurable; por defecto usa `App_Data/avatars` relativo al content root. La carpeta se crea al guardar y está excluida de Git. No se conserva el nombre ni el archivo original.
- **Subida y validación:** `POST /api/users/me/avatar`, autenticado, multipart con exactamente un campo archivo `file`. Hasta 5 MB, JPEG/PNG/WebP reales; MIME y extensión deben coincidir. Se rechazan archivos vacíos, corruptos, otros formatos, dimensiones inferiores a 64 px o superiores a 4096 px por lado, más de 16 millones de píxeles y formatos animados detectados por el decodificador. El tamaño del cuerpo se limita a 5 MB + 64 KB de overhead solo en esta ruta; las demás mantienen su límite previo. Se comparte `profile-write` con edición y privacidad.
- **Procesamiento:** SkiaSharp 4.156.0 decodifica con dimensiones comprobadas previamente. Corrige las ocho orientaciones EXIF, recorta al centro y produce un WebP estático de 256 × 256, calidad 85, sin metadatos originales. Como máximo dos imágenes se procesan simultáneamente por instancia.
- **Eliminación y avatar por defecto:** `DELETE /api/users/me/avatar` es idempotente y devuelve 204. Desvincula la foto y elimina el archivo previo. Reemplazar también elimina la imagen anterior. Sin foto o ante un error al cargarla, el frontend muestra las iniciales y su color determinista.
- **Componente de avatar:** perfil, búsqueda, amigos y solicitudes reciben `avatarUrl` real. Login/refresh incluyen la URL para la barra de navegación. `UserAvatarComponent` resuelve las URLs del backend incluso si frontend y API están en hosts distintos. El editor ofrece selección, vista previa, descarte, guardado, confirmación para quitar y manejo de errores y 429.

La foto se guarda por separado del nombre y la biografía; la interfaz lo explica. Cancelar la edición de texto no revierte una foto ya guardada. Durante una operación de avatar se bloquea el cierre del diálogo y el guardado del texto. La sesión y el perfil actualizan la foto sin renovar el JWT.

## Consistencia y entrega

`Users.AvatarFileName` es nullable y tiene un máximo de 37 caracteres. Las URLs son `/api/avatars/{guid}.webp`; no contienen ids de usuarios ni rutas del servidor. `GET` es público porque el avatar forma parte de los datos mínimos de un perfil, incluso restringido. Sirve exclusivamente claves válidas y contenido `image/webp`, con `nosniff` y caché de una hora. Cada reemplazo usa otra URL.

Las escrituras verifican `ConcurrencyStamp` y lo cambian junto con la referencia, de forma atómica. Un conflicto elimina el archivo nuevo y devuelve 409; una edición vieja de Identity no puede restaurar una foto eliminada. Una vez iniciada la actualización de base se termina aunque el cliente cancele, para conocer el resultado antes de borrar archivos. Se limpia el archivo previo después de guardar la nueva referencia.

Base y disco no comparten transacción. Una caída entre guardar archivo y actualizar base puede dejar un archivo huérfano; errores de permisos o I/O al limpiar se registran sin deshacer una actualización confirmada. El despliegue debe conservar la carpeta en un volumen persistente y respaldarla junto con la base. Si se usan varias instancias, deben compartir almacenamiento o reemplazar `IAvatarStorage` por un proveedor común.

## Migración

Se generó `20261113060115_AddUserAvatar` y el [SQL revisable](../sql/20261113060115_AddUserAvatar.sql). La migración se aplicó y se verificó en la base de desarrollo configurada el 13 de noviembre de 2026, sin migraciones pendientes. Solo agrega `Users.AvatarFileName varchar(37) NULL`; cuentas anteriores mantienen sus iniciales. En otros entornos, aplicar la migración antes de iniciar el backend actualizado:

```powershell
dotnet ef database update AddUserAvatar --project backend/src/Playtrough.Api
```

No ejecutar SQL y EF sobre la misma migración sin comprobar el historial. MySQL puede hacer commits implícitos en DDL.

## Comprobación

En «Mi perfil» → «Editar perfil», seleccionar una foto y comprobar que solo aparece la vista previa hasta pulsar «Guardar foto». Confirmar avatar nuevo en perfil y navegación, cerrar el diálogo y abrirlo otra vez. Guardar una bio y comprobar que conserva la foto. Probar reemplazo y «Quitar foto» con ambas opciones de confirmación. Comprobar imagen y luego iniciales desde otra cuenta en búsqueda, solicitudes y amigos, incluso con perfil privado.

Probar JPG con orientación EXIF, PNG con transparencia y WebP; SVG, GIF, bytes falsos con extensión PNG, imagen truncada, archivo de más de 5 MB y dimensiones inválidas. Los errores deben conservar la foto anterior. Desconectar red durante una subida: conservar selección y permitir reintento. Un 429 bloquea el reintento durante `Retry-After`, sin reenviar escrituras automáticamente.

`AvatarTests` cubre procesamiento real, API, permisos, exposición de URLs, reemplazo y eliminación física, conflictos, migración y Swagger. `avatar-editor.spec.ts` cubre la interacción, validaciones locales, errores, estados y liberación de URLs temporales. Hay pruebas de multipart, sesión y resolución de URLs del componente compartido.

Documentación primaria del procesador: [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.156.0), [SKCodec](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec).
