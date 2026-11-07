# Auditoría de secretos — fase 1

Fecha: 2026-10-21. Revisión de 239 archivos actuales y 125 commits alcanzables
desde las referencias locales de Git (617 combinaciones únicas de blob y ruta).
También se revisaron los mensajes de los commits y el `.env` local ignorado.
Se buscaron contraseñas, conexiones con contraseña, claves JWT/API, tokens,
credenciales SMTP y claves privadas. Los resultados se revisaron por contexto;
las coincidencias de fixtures, ejemplos y referencias a variables no se trataron
como credenciales reales. Es una búsqueda por patrones, no una garantía de ausencia
de secretos ni una comprobación de su vigencia. No se consultaron servicios con ellos.

## Credenciales expuestas que requieren revisión y rotación

| Archivo | Línea en ese commit | Commit de referencia | Tipo |
|---|---:|---|---|
| `docker-compose.yml` | 6 | `200672a4a689` | Contraseña MySQL root en texto plano |
| `docker-compose.yml` | 9 | `200672a4a689` | Contraseña del usuario MySQL en texto plano |
| `backend/docker-compose.yml` | 6 | `8c1d2c21199c` | Contraseña MySQL root tras mover el archivo |
| `backend/docker-compose.yml` | 9 | `8c1d2c21199c` | Contraseña del usuario MySQL tras mover el archivo |
| `odd/tasks/identity-initial-migration.md` | 79 | `78e7b5467d07` | Contraseña MySQL dentro de una salida de user-secrets documentada |

La última coincidencia seguía en el árbol de trabajo y fue retirada en esta fase.
Compose ya usaba variables de entorno. El historial no fue reescrito: rotar las
credenciales de MySQL que se hayan usado y actualizar su configuración externa.
Los commits identifican ubicaciones históricas, no afirman que sigan vigentes.

## Ejemplos y resultados restantes

- `src/Playtrought.Api/appsettings.json` y `appsettings.Development.json`, líneas
  3 y 6 en `ab802b7787fe`: placeholders de contraseña de conexión y clave JWT.
  Reaparecen bajo `backend/src/Playtrought.Api/` en `8c1d2c21199c`.
- Los ejemplos de contraseñas en pruebas, solicitudes HTTP y documentación son
  datos de prueba. Si alguno se reutilizó en una cuenta real, debe cambiarse.
- No se detectaron credenciales SMTP reales, claves API, claves privadas ni tokens
  de acceso reales mediante los patrones revisados.
- No hay workflows de GitHub Actions versionados que revisar. No se agregó CI nuevo.
- Los `appsettings` actuales usan contraseñas y clave JWT vacías; la conexión queda
  vacía para exigir configuración externa. `appsettings.Example.json` es ilustrativo.
- `.env` ya estaba ignorado; se agregaron `secrets.json` y `appsettings.*.local.json`.
  `backend/.env` contiene contraseñas MySQL locales en las líneas 1 (root) y 4
  (usuario) para Docker Compose y se conserva
  ignorado, sin mostrar ni modificar sus valores. Los almacenes externos de
  user-secrets no se leyeron ni modificaron.

## Prevención

`CommittedConfigurationTests` enumera con Git todos los `appsettings*.json`
versionados y comprueba sus contenidos del árbol de trabajo. Rechaza claves JWT,
contraseñas y contraseñas dentro de conexiones no vacías; informa únicamente
archivo y clave. Corre con `dotnet test backend/Playtrough.sln` y puede usarse en CI.

Login y registro ahora registran la operación y el tipo de excepción, siguiendo
la convención del correo y la recuperación. No envían al logger la excepción
original ni su mensaje, que podrían contener credenciales, tokens o enlaces.
`AuthLoggingTests` verifica mensajes, argumentos estructurados y ausencia de la
excepción original usando datos señuelo. No se habilitó logging de cuerpos,
headers HTTP, parámetros sensibles de EF ni protocolos SMTP.
