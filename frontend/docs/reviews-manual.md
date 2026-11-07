# Reseñas: comprobación manual del frontend

## Preparación

- Levantar el backend y el frontend (`npm start` desde `frontend`). Verificar `apiUrl` en el environment utilizado.
- Disponer de dos usuarios autenticados y un juego del catálogo. Para probar «Cargar más», usar un juego con más de 20 reseñas públicas.
- La navegación nueva usa el ID local: `/games/local/:gameId/reviews`. La ruta `/games/:externalId/reviews` admite IDs de GameBrain. Las consultas y escrituras de reseñas usan el `id` local devuelto por el detalle.

## Recorrido con el backend

1. Con el usuario A, buscar un juego y abrir su ficha. Comprobar promedio, cantidad y «Mi reseña». En un juego sin reseñas debe aparecer «Sin reseñas todavía» y «Escribir reseña».
2. Pulsar «Escribir reseña». El mismo modal muestra «Tu reseña de X» y enfoca el título. Probar Tab, flechas, Inicio y Fin en las estrellas. Sin puntuación no se puede publicar; el texto es opcional.
3. Elegir una puntuación y escribir texto con saltos de línea. Probar también `<b>hola</b>` o `<script>alert(1)</script>`: deben verse literalmente y no ejecutarse. Publicar y comprobar el aviso, la reseña y el nuevo promedio/cantidad.
4. Editar: comprobar la precarga y que «Guardar cambios» esté deshabilitado mientras no haya cambios. Modificar la puntuación o el texto y guardar. Debe aparecer «Reseña actualizada» y la marca «(editada)».
5. Editar de nuevo y escribir un borrador. Probar «Volver», «Cancelar» y Esc: deben pedir confirmación en línea. «Seguir editando» conserva el borrador; «Descartar» vuelve al detalle y enfoca el botón de origen. Sin cambios, Esc vuelve directamente al detalle; un segundo Esc cierra el modal.
6. Probar el límite: 2.000 emojis cuentan como 2.000 caracteres; uno adicional deshabilita el envío y muestra «Límite excedido». Los espacios de los extremos y los controles que elimina el backend no cuentan.
7. Pulsar «Ver reseñas (N)». El modal se cierra, no devuelve el foco a la tarjeta y navega a la URL del juego. El título de la pantalla recibe el foco y «Juegos» sigue activo en la navegación móvil.
8. Comprobar que la reseña propia aparece arriba una sola vez y solo para lectura, con «Podés editarla desde la ficha del juego». No debe haber acciones de edición ni borrado en esta pantalla.
9. Alternar «Más recientes», «Mejor puntuadas» y «Peor puntuadas». Verificar `?sort=recent`, `?sort=top` y `?sort=low`; cada cambio comienza en la primera página. Con una conexión lenta, cambiar rápido de orden: una respuesta anterior no debe reemplazar los resultados actuales.
10. Probar «Cargar más»: agrega resultados sin duplicados, se deshabilita durante la petición y desaparece al llegar al final. Una reseña sin texto no muestra un texto de relleno; conserva las estrellas y los datos del autor/fecha. Probar «Ver más / Ver menos» con texto largo o muchas líneas.
11. Usar «Volver»: tras navegar desde la aplicación, vuelve al historial anterior. Abrir la URL directamente en una pestaña nueva, incluso con `?sort=low`: «Volver» lleva a `/games`. Cambiar el orden no debe agregar pasos al historial. Sin sesión, la URL redirige al login conservando `returnUrl`.
12. En otra sesión/navegador, con el usuario B, reseñar el mismo juego. Volver a abrir la ficha o recargar la pantalla del usuario A: deben aparecer el promedio/cantidad actualizados y la reseña de B con su username. B debe ver su propia reseña arriba y la de A en la lista.
13. Con A, volver a la ficha y pulsar «Eliminar». «Cancelar» no elimina nada. Confirmar debe mostrar «Reseña eliminada», restaurar «Escribir reseña» y actualizar promedio/cantidad. Si ya fue eliminada desde otra sesión, se trata como éxito y se vuelve a consultar el detalle para obtener el resumen real.
14. Con DevTools, probar sin conexión y peticiones lentas. Verificar mensajes con reintento y conservación del borrador. Si el backend devuelve 429 con `Retry-After`, comprobar la cuenta regresiva y que no se reintente automáticamente. Para códigos concretos (`invalid_rating`, `invalid_text`, `concurrent_modification`, `game_not_found`, `catalog_unavailable`), usar un entorno de prueba que devuelva ese error; los tests unitarios cubren esos contratos.

## Decisiones y límites

- Se confirmó seleccionar cinco valores enteros (1 a 5) y mostrar decimales en lectura. Si una reseña previa tiene 3,5, editar únicamente su texto conserva ese valor; elegir una estrella lo reemplaza por el entero elegido.
- El contador reproduce la normalización del backend: CRLF/CR a LF, eliminación de controles salvo tabulación y salto de línea, recorte de espacios Unicode y conteo por valores escalares Unicode. No cuenta unidades UTF-16.
- El orden predeterminado es `recent`; un valor desconocido usa ese mismo orden. Los cambios de orden reemplazan la URL actual para conservar el destino de «Volver».
- «Sin reseñas» depende del total del backend. Si solo existe la reseña propia, la lista indica que todavía no hay reseñas de otros jugadores.
- Las fechas se muestran como `dd/mm/aaaa` en la zona horaria del navegador. Si existe `editedAt`, se muestra esa fecha y «(editada)».
- Las notificaciones del servicio actualizan las vistas de la misma aplicación. No hay sincronización en tiempo real entre navegadores o usuarios: para ver escrituras de otra sesión, volver a cargar la información.
- Los usernames son texto sin enlace. El template conserva un TODO para la futura épica de perfiles. No hay un ítem propio de reseñas en el menú: se retiró el placeholder «Reseñas, próximamente» y la barra móvil conserva Juegos, Biblioteca y Perfil.
- Las verificaciones visuales usan respuestas de API simuladas; no sustituyen este recorrido con el backend real.

## Verificación automatizada

Desde `frontend`:

```powershell
npm run build
npm test -- --watch=false
```

En el entorno aislado de Codex, si Vitest no puede leer sus archivos temporales, usar una carpeta temporal dentro del proyecto para esa ejecución:

```powershell
$env:TEMP = Join-Path (Get-Location) '.angular\test-temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
npm test -- --watch=false
```

Los tests son unitarios/de integración de Angular y HTTP simulado. No se añadieron tests end-to-end ni cambios del backend.
