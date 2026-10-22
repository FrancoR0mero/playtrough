using Microsoft.Extensions.Options;

namespace Playtrough.Api.Models;

internal static class SafeOptionsBinding
{
    public static OptionsBuilder<T> BindSafely<T>(this OptionsBuilder<T> options, IConfigurationSection section)
        where T : class
        => options.Configure(settings =>
        {
            try { section.Bind(settings); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or OverflowException)
            {
                // Conversion failures (e.g. an invalid enum) can echo the input in
                // their inner exception. Report only the section's configured key.
                throw new OptionsValidationException(options.Name, typeof(T),
                    [$"{section.Path}: configuration contains an invalid value type; check the documented formats."]);
            }
        });
}
