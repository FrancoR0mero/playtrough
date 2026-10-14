using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Playtrought.Api.Models;

namespace Playtrought.Tests;

internal static class IdentityTestFactory
{
    public static Mock<UserManager<ApplicationUser>> UserManager()
        => new(Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(), Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), Mock.Of<IServiceProvider>(), NullLogger<UserManager<ApplicationUser>>.Instance);

    public static Mock<SignInManager<ApplicationUser>> SignInManager(UserManager<ApplicationUser>? users = null)
        => new(users ?? UserManager().Object, Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance, Mock.Of<IAuthenticationSchemeProvider>(),
            new DefaultUserConfirmation<ApplicationUser>());
}
