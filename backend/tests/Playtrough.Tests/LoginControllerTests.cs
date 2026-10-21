using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Playtrough.Api.Controllers;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class LoginControllerTests
{
    [Theory]
    [InlineData(AuthError.InvalidCredentials, 401, "The email/username or password is incorrect.")]
    [InlineData(AuthError.AccountLocked, 423, "This account is temporarily locked after too many failed login attempts. Try again in 15 minutes.")]
    [InlineData(AuthError.Unexpected, 500, null)]
    public async Task Login_FailuresAreProblemDetails(AuthError error, int status, string? detail)
    {
        var service = new Mock<IAuthService>();
        service.Setup(s => s.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<LoginResponseDto>.Fail(error));
        var controller = new AuthController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var response = Assert.IsType<ObjectResult>(await controller.Login(new(), default));
        Assert.Equal(status, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(detail, problem.Detail);
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("ana", null)]
    [InlineData("ana", "")]
    public void Login_MissingRequiredFieldsAreInvalid(string? identifier, string? password)
    {
        var dto = new LoginRequestDto { Identifier = identifier!, Password = password! };
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }
}
