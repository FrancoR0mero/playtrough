# Confirmación de correo

El registro conserva el 201 y agrega `emailConfirmationRequired: true`, sin crear
una sesión. El correo se envía después de guardar la cuenta; un fallo de entrega
se registra sin dirección, token, cuerpo ni detalle de la excepción y conserva el
registro exitoso. `IEmailSender` mantiene su interfaz.

Configuración:

- `PasswordReset:FrontendBaseUrl`: URL base compartida por los enlaces de recuperación
  y confirmación; en Development es `http://localhost:4200`.
- `EmailConfirmation:TokenLifespanMinutes`: 1440 (24 horas), con provider independiente.
- `EmailConfirmation:ResendCooldownSeconds`: 60, por ID de usuario.
- `PasswordReset:TokenLifespanMinutes`: conserva 60 minutos.

El reenvío encola todas las solicitudes válidas sin consultar la cuenta durante la
respuesta HTTP. Un worker con un único lector consulta la cuenta y verifica el
cooldown antes de enviar. La cola y la caché son locales al proceso: se pierden al
reiniciar y no se comparten entre réplicas. El cooldown comienza con el primer
reenvío; el correo inicial del registro no lo consume. Un fallo de envío también
consume ese intervalo. No hay reintentos automáticos.

La confirmación repetida de una cuenta ya confirmada devuelve éxito. Generar otro
token no invalida por sí solo los enlaces anteriores; siguen las reglas de Identity.

## Migración de cuentas existentes

Se creó `20261017000000_ConfirmExistingUserEmails`, sin aplicarla a la base real:

```sql
UPDATE `Users` SET `EmailConfirmed` = 1 WHERE `EmailConfirmed` = 0;
```

Aplicarla durante el despliegue, antes de habilitar nuevos registros, para que solo
las cuentas anteriores queden exceptuadas de confirmar su correo. `Down` no revierte
confirmaciones porque no puede distinguir las realizadas por la migración de las
realizadas por usuarios. El seed solo contiene roles. Identity y el índice de
`NormalizedEmail` ya exigen emails únicos.

## Prueba manual local

1. Iniciar MySQL y MailHog con la configuración local existente:

   ```powershell
   docker compose -f backend/docker-compose.yml --profile development up -d --wait
   dotnet run --project backend/src/Playtrough.Api
   ```

2. Desde otra terminal, registrar una cuenta con un email y username nuevos:

   ```powershell
   $api = 'http://localhost:5000/api/auth'
   $email = 'confirmacion_prueba@example.com'
   $password = 'PruebaSegura123!'
   Invoke-RestMethod "$api/register" -Method Post -ContentType 'application/json' -Body (@{
       email = $email; username = 'confirmacion_prueba'; password = $password
   } | ConvertTo-Json)
   ```

   Verificar 201 y `emailConfirmationRequired: true`, sin tokens de sesión.

3. Antes de confirmar, enviar un POST a `/api/auth/login` con
   `{"identifier":"confirmacion_prueba@example.com","password":"PruebaSegura123!"}`.
   Debe devolver 403 ProblemDetails con `code: "email_not_confirmed"`.
   Una contraseña incorrecta debe devolver el 401 genérico.

4. Abrir la bandeja de MailHog en <http://localhost:8025> y copiar el enlace completo.
   El enlace apunta al frontend, que está fuera del alcance de esta implementación.
   Para probar la API sin pantalla de frontend, extraer sus parámetros así:

   ```powershell
   $link = [uri](Read-Host 'Pegá el enlace del correo')
   $parameters = @{}
   foreach ($pair in $link.Query.TrimStart('?').Split('&')) {
       $parts = $pair.Split('=', 2)
       $parameters[$parts[0]] = [uri]::UnescapeDataString($parts[1])
   }
   $confirmation = @{ email = $parameters.email; token = $parameters.token }
   ```

5. Antes de usar el token válido, enviar una copia con el token alterado:

   ```powershell
   Invoke-RestMethod "$api/confirm-email" -Method Post -ContentType 'application/json' -Body (@{
       email = $confirmation.email; token = $confirmation.token + 'manipulado'
   } | ConvertTo-Json)
   ```

   Debe devolver 400 con el mensaje genérico de enlace inválido o vencido. Probarlo
   antes de confirmar: las cuentas ya confirmadas responden 200 por idempotencia.

6. Confirmar con el token válido y repetir exactamente la solicitud; ambas devuelven 200:

   ```powershell
   Invoke-RestMethod "$api/confirm-email" -Method Post -ContentType 'application/json' -Body ($confirmation | ConvertTo-Json)
   Invoke-RestMethod "$api/confirm-email" -Method Post -ContentType 'application/json' -Body ($confirmation | ConvertTo-Json)
   Invoke-RestMethod "$api/login" -Method Post -ContentType 'application/json' -Body (@{
       identifier = $email; password = $password
   } | ConvertTo-Json)
   ```

   El login ahora devuelve 200 con tokens.

7. Para otra cuenta pendiente, enviar dos POST a `/api/auth/resend-confirmation`
   con `{"email":"SU_EMAIL"}`. Ambos devuelven el mismo 200; solo el primero envía.
   Pasados 60 segundos, se permite otro envío. Un email inexistente o confirmado
   recibe el mismo body. Un body sin email o con formato inválido devuelve 400.

## Verificación automatizada

```powershell
dotnet build backend/Playtrough.sln --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
```

Los tests nuevos usan `IEmailSender` mockeado, Identity real y SQLite aislado, como
los de recuperación. Cubren los flujos de servicio y HTTP, expiración de token,
duraciones independientes, cooldown con reloj controlado y el SQL de datos sobre
una base aislada. No envían SMTP ni aplican migraciones sobre MySQL real.

Los usuarios existentes de tests se marcaron confirmados únicamente en
`LoginApiFactory.CreateUserAsync`, `AuthServiceTestContext` y el fixture `_user`
de `AuthServiceLoginTests`. El resto del ajuste de tests existentes es el cableado
de dependencias y soporte del nuevo worker/reloj en la factory.
