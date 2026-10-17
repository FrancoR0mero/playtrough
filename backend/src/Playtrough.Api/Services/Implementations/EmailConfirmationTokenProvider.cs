using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions;

public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider protection,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(protection, options, logger)
{
    public const string ProviderName = "EmailConfirmation";
}
