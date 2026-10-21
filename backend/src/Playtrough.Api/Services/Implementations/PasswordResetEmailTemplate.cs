using System.Net;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

internal static class PasswordResetEmailTemplate
{
    public static EmailContent Create(string username, string url, int lifetimeMinutes)
    {
        return AccountEmailTemplate.Create(username, url, "Restablecé tu contraseña",
            "Recibimos una solicitud para restablecer tu contraseña de Playtrough.", "Restablecer contraseña",
            $"El enlace tiene una vigencia de {lifetimeMinutes} minutos y solo puede usarse una vez.",
            "Si no lo pediste, ignorá este correo. Tu contraseña no cambiará hasta que completes el restablecimiento.",
            "Abrí este enlace para restablecerla");
    }
}

internal static class AccountEmailTemplate
{
    public static EmailContent Create(string username, string url, string heading, string introduction,
        string action, string validity, string disclaimer, string? textAction = null)
    {
        var safeName = WebUtility.HtmlEncode(username);
        var safeUrl = WebUtility.HtmlEncode(url);
        var html = $"""
            <!doctype html>
            <html lang="es"><body style="font-family:Arial,sans-serif;background:#0f172a;color:#e2e8f0;padding:24px;">
            <div style="max-width:560px;margin:auto;padding:24px;border:1px solid #475569;border-radius:12px;">
              <h1 style="font-size:24px;color:#ffffff;">{heading}</h1>
              <p>Hola, {safeName}:</p>
              <p>{introduction}</p>
              <p><a href="{safeUrl}" style="display:inline-block;padding:12px 20px;background:#4f46e5;color:#ffffff;text-decoration:none;border-radius:6px;">{action}</a></p>
              <p>{validity}</p>
              <p>Si el botón no funciona, copiá este enlace en tu navegador:<br><a href="{safeUrl}" style="color:#a5b4fc;word-break:break-all;">{safeUrl}</a></p>
              <p>{disclaimer}</p>
            </div></body></html>
            """;
        var text = $"""
            Hola, {username}:

            {introduction}
            {textAction ?? action}:
            {url}

            {validity}
            {disclaimer}
            """;
        return new EmailContent($"{heading} de Playtrough", html, text);
    }
}
