# Biblioteca personal: fase 1

Implementa US-23, US-24, US-25 y US-27.3 dentro del modal del catálogo.
La pantalla `/library` y sus menús quedan para la fase 2.

## Decisiones

- La consulta comienza cuando el detalle entrega `id`, el identificador local. El
  `externalId` sigue siendo exclusivo de la consulta del catálogo.
- `LibraryService.changes$` emite `added`, `status-changed` o `removed` solamente
  después de una respuesta exitosa. Altas y cambios incluyen el DTO del servidor;
  bajas incluyen `gameId`. No hay caché global de bibliotecas de usuarios.
- Los cuatro estados y sus etiquetas/estilos viven en `LIBRARY_STATUSES`.
- El botón **Actualizar estado** confirma juntos estado y fecha. Se permite enviar
  el mismo estado, conforme al contrato del backend.
- Sin **Indicar fecha**, se omite `occurredOn`. El campo de fecha mantiene el valor
  calendario local `YYYY-MM-DD`; los argumentos `Date` del servicio se serializan
  con `getFullYear`, `getMonth` y `getDate`. No se usa conversión UTC.
- Una baja se confirma dentro del modal. Cancelar devuelve el foco a **Quitar de
  mi biblioteca**; una baja exitosa lo devuelve al selector de estados.
- Los conflictos recargan la entrada antes de habilitar una nueva operación.
  Cambiar de juego o destruir el modal cancela sus suscripciones pendientes.
- Los errores 401 siguen a cargo del interceptor; los 429 reutilizan su error
  tipado y la cuenta regresiva existente, sin reintentos automáticos.

## Verificación automatizada

Desde `frontend`:

```sh
npm run build
npm test -- --watch=false
npm run lint
```

Se agregan tests del servicio, fechas, sección de biblioteca e integración con el
modal y el interceptor. La prueba de fecha nocturna representa las 23:30 de
Argentina y comprueba que se envía el día local, aunque en UTC sea el siguiente.
Los tests existentes `game-detail.spec.ts` y `games.spec.ts` simulan únicamente la
consulta de biblioteca para mantener aisladas las verificaciones del catálogo.
Los tests de integración nuevos usan HTTP real simulado por `HttpTestingController`.

## Prueba manual con backend

1. Iniciar sesión, abrir `/games`, buscar un juego y abrir su detalle.
2. Si no está en la biblioteca, verificar que no aparece un error. Elegir un estado
   con clic o con las flechas del teclado y agregarlo sin indicar fecha.
3. Verificar estado, fecha y anuncio de éxito. Cambiar el estado y confirmar con
   **Actualizar estado**, primero sin fecha y luego con **Indicar fecha**.
4. En otro juego, agregar indicando una fecha pasada. Verificar que el campo
   comienza en hoy y no permite una fecha futura; al ocultarlo, la petición no
   debe incluir `occurredOn` (se puede revisar en Network).
5. Intentar un cambio con fecha anterior al último evento: debe aparecer el límite
   recibido en `extensions.minDate`, en formato `dd/mm/aaaa`.
6. Pulsar **Quitar de mi biblioteca**, cancelar y comprobar que no se hace DELETE.
   Repetir y confirmar: debe volver al estado sin entrada, dentro del mismo modal.
7. Cerrar el detalle con una petición pendiente y abrir otro juego. No deben
   aparecer datos ni avisos del juego anterior.
8. Para conflictos, modificar el mismo juego desde otra sesión y repetir una
   operación con datos desactualizados. Si el backend devuelve uno de los códigos
   de conflicto, debe recargarse el estado y mostrarse el aviso correspondiente.

Pestañas, buscador de biblioteca, menús de tarjetas y contadores se verificarán al
implementar la fase 2.
