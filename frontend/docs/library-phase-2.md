# Biblioteca personal: fase 2 y prueba completa

Implementa US-26 y reutiliza las acciones de US-23, US-24, US-25 y US-27.3.

## Implementación y decisiones

- `/library` conserva el `authGuard` existente. El enlace de navegación ahora se
  llama **Mi biblioteca**, visible para las cuentas autenticadas.
- Los filtros se restauran desde `status`, `q` y `sort`. Ausencia o valores
  desconocidos de estado/orden usan **Todos** y **Cambios recientes**. Al cambiar
  filtros se escriben los valores normalizados en la URL. Las búsquedas usan
  `replaceUrl` para no generar una entrada de historial por tecla.
- La búsqueda espera 400 ms, pero cancela la petición anterior inmediatamente al
  escribir. No se impone el mínimo de dos caracteres del catálogo: el contrato de
  biblioteca no lo exige. `invalid_query` se muestra como error accesible.
- La tarjeta compartida conserva imagen, título, año, botón nativo y proporción
  del catálogo. En biblioteca agrega un distintivo con texto sobre fondo oscuro
  opaco y un menú separado del botón del detalle.
- El estado actual del menú está marcado y deshabilitado. Los cambios usan la
  fecha local de hoy; para otra fecha se abre el detalle. Quitar utiliza el
  contenido de confirmación extraído del diálogo de administración de roles,
  conservando CDK, foco inicial en Cancelar y retorno del foco.
- Los contadores siempre representan toda la biblioteca, aunque haya filtros.
  Los eventos exitosos actualizan contadores y tarjetas localmente. Cuando falta
  el estado previo o los contadores serían inconsistentes, se consulta de nuevo.
- `LibraryService.invalidated$` comunica escrituras fallidas, además de
  `changes$` para éxitos. Así también se actualiza la lista detrás de un modal
  que recibió un conflicto. Los 401 continúan a cargo del interceptor.
- Ante un 429 en una escritura, se respeta `Retry-After` y al terminar se realiza
  una consulta GET de actualización. **La escritura no se reintenta**. Los errores
  de carga del listado tienen un botón de reintento manual.
- En **Cambios recientes**, o al insertar un juego en la lista visible, se
  conserva la actualización local mientras se consulta el orden del servidor.
  No se deducen desempates a partir de fechas sin hora. Tras otras modificaciones,
  **Cargar más** vuelve a consultar las páginas anteriores antes de avanzar:
  las bajas pueden desplazar los offsets y, sin esta precaución, omitir juegos.
- Al cerrar un detalle, el foco vuelve a la tarjeta de origen. Si hubo una
  recarga, se busca su nueva instancia; si dejó de pertenecer a la pestaña,
  vuelve a la pestaña activa.

## Validación

Desde `frontend`:

```sh
npm test -- --watch=false
npm run build
npm run lint
```

La suite cubre filtros, URL real, debounce/cancelación, pestañas y menús con
teclado, paginación, estados vacíos, errores, confirmación, actualización desde
el modal, recargas por conflicto y recuperación del foco. Los tests de la fase 1
y los existentes de catálogo y administración siguen ejecutándose.

Ajustes mínimos en tests existentes:

- `app.routes.spec.ts`, `auth-navigation.spec.ts` y `games-routing.spec.ts`:
  simulan únicamente la nueva consulta de biblioteca, para conservar el alcance
  de sus verificaciones de autenticación y navegación.
- `layout.spec.ts`: espera la etiqueta **Mi biblioteca**.
- `services/library.spec.ts`: comprueba que una escritura fallida notifica la
  invalidación sin emitir un cambio exitoso.

No se agregaron tests end-to-end ni se modificó el backend.

## Dependencia para la prueba manual

Se necesita un backend que exponga el contrato completo `/api/library` indicado
en el pedido. Al revisar este checkout, hay servicios/repositorios de biblioteca,
pero no un controlador ni rutas HTTP de biblioteca registradas; el frontend se
verificó con HTTP simulado. La prueba manual contra un servidor real queda
pendiente de disponer de esos endpoints. La URL se toma de los `environments`
(actualmente `http://localhost:5000/api`).

## Pasos manuales con el backend disponible

1. Ejecutar `npm start` desde `frontend`, iniciar sesión y abrir `/games`. Buscar
   un juego y abrir el detalle con clic o Enter. Elegir un estado y agregarlo sin
   fecha; comprobar el mensaje, el estado y la fecha devuelta por el servidor.
2. Cambiarlo a otro estado sin fecha y luego con **Indicar fecha**. En Network,
   verificar que `occurredOn` se omite cuando el selector está oculto y que, cuando
   se envía, conserva el día local incluso durante la noche de Argentina.
3. Elegir una fecha anterior al último cambio y cambiar a otro estado. Verificar
   el aviso con el límite `dd/mm/aaaa`; corregir la fecha y volver a guardar.
4. Quitar desde el detalle: primero cancelar y comprobar que no se envía DELETE;
   después confirmar. La confirmación debe permanecer dentro del mismo modal.
5. Agregar al menos dos juegos en distintos estados y abrir **Mi biblioteca**.
   Recorrer Todos, Jugando, Backlog, Terminados y Completados con clic y flechas.
   Los contadores deben mantenerse globales y `status` debe cambiar en la URL.
6. Buscar por nombre, cambiar entre los cuatro órdenes, recargar la página y
   volver a una URL anterior. Verificar que texto, orden y pestaña se restauran.
   Probar una búsqueda sin resultados y una pestaña sin juegos.
7. Abrir el menú de una tarjeta con teclado; recorrerlo con flechas, Home y End,
   cerrarlo con Escape o clic fuera. Cambiar a otro estado y comprobar que baja
   el contador anterior, sube el nuevo y el total permanece igual. En una pestaña
   específica, la tarjeta debe desaparecer si dejó de pertenecer a ella.
8. Quitar desde el menú: cancelar una vez, luego confirmar. Comprobar que bajan
   el contador del estado y el total. Al quitar el último juego debe aparecer
   **Todavía no agregaste juegos** con enlace al catálogo.
9. Abrir el detalle desde una tarjeta de biblioteca y cambiar su estado. Cerrar
   el modal y verificar los mismos contadores y distintivo actualizados. El foco
   debe volver a la tarjeta o a la pestaña si la tarjeta desapareció.
10. Con más de 20 juegos, pulsar **Cargar más** varias veces rápidamente: no debe
    haber peticiones dobles ni tarjetas duplicadas. Quitar o mover un juego antes
    de continuar; comprobar que ningún juego queda omitido por el desplazamiento.
11. Probar un conflicto desde dos sesiones y un 429 con `Retry-After`. Verificar
    los avisos accesibles, la recarga de lista y que no se repite la escritura.
12. Cerrar sesión y abrir `/library` directamente: debe redirigir al login,
    conservando la URL de retorno.
