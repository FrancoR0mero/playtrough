# Perfiles, amistades, búsqueda y privacidad: comprobación manual

## Preparación

- Levantar el backend con la base de desarrollo y sus migraciones aplicadas. Desde `frontend`, ejecutar `npm start`. Comprobar el `apiUrl` del environment utilizado (actualmente `http://localhost:5000/api`).
- Usar tres cuentas con email confirmado: A, B y C. Abrir cada cuenta en un perfil de navegador independiente para no compartir `localStorage`. Las pestañas del mismo perfil comparten sesión; no sirven para mantener tres usuarios distintos.
- Empezar sin amistades ni solicitudes pendientes entre las cuentas, o completar/cancelar las existentes desde la interfaz. No modificar directamente la base para este recorrido.

## Recorrido con tres usuarios

1. **A: perfil propio.** Abrir «Mi perfil», editar la bio con saltos de línea y texto como `<b>hola</b>`. Guardar: el texto se muestra literalmente, aparece «Perfil actualizado» y el foco vuelve a «Editar perfil». Probar 300 y 301 emojis: se cuentan caracteres Unicode y se bloquea guardar al superar el límite.
2. **A: nombre de usuario.** Elegir un nombre disponible de 3 a 20 letras ASCII, números o guiones bajos. Guardar: comprobar el refresh de sesión, el nuevo nombre en navegación y `/profile` como URL. Reabrir el diálogo: si existe una espera del backend, el nombre queda deshabilitado y muestra su fecha. Cambiar solo mayúsculas no constituye un cambio. Mantener el nombre nuevo de A en el resto del recorrido.
3. **B: encontrar a A.** En Juegos escribir parte del nombre de A y cambiar a «Personas»: se conserva `q`. Con menos de dos caracteres no se consulta; al escribir más se espera unos 400 ms. Abrir el perfil desde el resultado y volver. La ruta `/profile/MI_NOMBRE` de la cuenta propia redirige a `/profile` sin distinguir mayúsculas.
4. **B: enviar y cancelar.** Pulsar «Agregar amigo» en A. Ver «Solicitud enviada», la pestaña Enviadas y el contador correspondiente. Cancelar y comprobar que la solicitud sale de la lista. Enviar una solicitud nueva a A.
5. **A: aceptar.** Volver a la pestaña o abrir Amigos. El indicador anuncia la cantidad de solicitudes recibidas. En Recibidas, aceptar a B: desaparece la solicitud, se actualiza el contador y aparece «Solicitud aceptada». Ambos usuarios se ven en Amigos, con enlace al perfil y fecha de amistad.
6. **C y A: rechazar.** C envía una solicitud a A. A la rechaza desde Recibidas: desaparece y se confirma solo a A. C no recibe una notificación de rechazo; al volver a abrir Enviadas ya no debe aparecer la solicitud. Si C intenta enviarla otra vez antes del plazo configurado por el backend, se muestra «Podrás enviar otra solicitud el …».
7. **Pestañas y búsqueda.** En Amigos, recorrer Amigos/Recibidas/Enviadas con flechas, Inicio y Fin; comprobar `?tab=`. Buscar a B dentro de Amigos y verificar `?q=`. Al cambiar rápidamente el texto con una conexión lenta, los resultados viejos no deben reemplazar la búsqueda actual.
8. **Perfil ajeno.** B abre el perfil de A: ve avatar, bio, fecha, contador y hasta seis amigos; «Ver todos» abre la lista de solo lectura. No hay acciones de gestión en esa lista. Los autores de las reseñas también enlazan a su perfil.
9. **A: perfil público.** En Privacidad, elegir Perfil → Público y guardar. A, B (amigo) y C (sin amistad) ven el contenido completo del perfil de A. Todos los visitantes deben estar autenticados.
10. **A: solo amigos.** Cambiar Perfil → Solo amigos. Al volver al perfil de A, B sigue viendo el contenido; C ve únicamente username, avatar, botón de amistad y «El contenido de este perfil es solo para amigos». No aparecen bio, fecha ni contadores. Acceder directamente a `/profile/NOMBRE_DE_A/friends` con C muestra el mismo aviso y un enlace de vuelta.
11. **A: privado.** Cambiar Perfil → Privado. B y C ven únicamente los datos públicos mínimos y «Este perfil es privado», incluso aunque B sea amigo. A continúa viendo y editando su perfil. Los perfiles privados siguen apareciendo en búsqueda y permiten solicitudes.
12. **Eliminar amistad.** A vuelve a Perfil → Solo amigos. B abre A y pulsa «Eliminar amistad». «Conservar amistad» no modifica nada y devuelve el foco al botón; «Sí, eliminar» elimina la amistad. Tras consultar de nuevo el perfil, B pierde acceso a los datos sensibles y ve el aviso de solo amigos. Verificar también la eliminación desde la lista Amigos.
13. **Cambio desde otra pestaña.** B vuelve a enviar una solicitud a A. Mantener el perfil de A abierto en B, y aceptar desde la sesión de A. Al recuperar el foco en B se vuelve a consultar el perfil y aparece el contenido autorizado. Si la ventana permanece visible, se comprueba cada 30 segundos. Repetir cambiando la privacidad de A o eliminando la amistad; el contenido se oculta cuando el servidor confirma la restricción.
14. **Historial.** A prueba Público, Solo amigos y Privado en Historial. Se precarga la elección guardada al volver al perfil, pero estas preferencias todavía no restringen biblioteca ni reseñas; la interfaz lo aclara. El botón de guardar permanece deshabilitado sin cambios.

## Errores, teclado y paginación

- Recorrer ambas preferencias con Tab, flechas, Inicio y Fin. Los radios se anuncian bajo Perfil o Historial y los controles tienen objetivos táctiles de al menos 44 px.
- Desconectar la red con DevTools: los formularios conservan los valores, no anuncian éxito y permiten reintentar. Si el perfil se guardó y solo falla el refresh por red, se muestra un aviso sin cerrar la sesión.
- Con una respuesta 429 y `Retry-After`, verificar el contador y el bloqueo temporal del reintento. No hay reenvíos automáticos de escrituras.
- Para comprobar `Cargar más`, usar datos de prueba con más de 20 resultados/amigos/solicitudes; tres cuentas alcanzan para los flujos funcionales, pero no para generar dos páginas. Los tests unitarios cubren duplicados, peticiones simultáneas, cancelación y reintento de una página fallida.
- Probar rutas privadas sin sesión: deben llevar al login. Una cuenta no confirmada ajena no es un resultado visible del backend.

## Decisiones y límites

- Un solo componente maneja el perfil propio y ajeno. Los campos sensibles se ocultan por `isRestricted`, aunque una respuesta contenga esos campos por error; también se admiten campos omitidos y `null`.
- Los errores aceptan códigos y metadatos tanto en la raíz de ProblemDetails (respuesta real del backend) como dentro de `extensions`.
- Los datos de acceso se reconsultan tras acciones de amistad, al recuperar el foco, al volver a una pestaña visible y cada 30 segundos mientras está visible. No se usan notificaciones en tiempo real ni conexiones persistentes. Las preferencias propias no se recargan automáticamente mientras se editan para preservar cambios sin guardar.
- El 403 de la lista de amigos no incluye el motivo de privacidad: se consulta el perfil para mostrar «privado» o «solo para amigos». Si esa consulta también falla, se muestra un aviso genérico de acceso restringido, sin datos de la lista.
- La confirmación para eliminar una amistad es en línea. Las listas públicas reutilizan la fila de usuario sin acciones de gestión. La búsqueda reutiliza el botón de amistad con presentación compacta y objetivos táctiles completos.
- No se implementan subida de avatar, bloqueo, feed, cambios de contraseña/email, eliminación de cuenta ni restricciones de historial sobre biblioteca/reseñas. No se modificó el backend ni se añadieron tests E2E.

## Verificación automatizada

Desde `frontend`: `npm run build`, `npm run lint` y `npm test -- --watch=false`.
La revisión visual usa capturas locales con respuestas ficticias y no sustituye este recorrido contra el backend.

En la fase 4 se ajustaron los tests existentes `profile.spec.ts` y `profile-view.spec.ts` para proveer la lectura de privacidad; `app.routes.spec.ts` para responder al nuevo GET; `friends.spec.ts` y `friends-preview.spec.ts` para comprobar los avisos de restricción. Se amplió `services/users.spec.ts` con el contrato GET/PUT de privacidad. Los tests nuevos de configuración viven en `profile-privacy.spec.ts`.
