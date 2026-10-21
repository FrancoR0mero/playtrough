using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Playtrough.Api.Authorization;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class AdminUserServiceTests
{
    private readonly Mock<IAdminUserRepository> _repository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<UserManager<ApplicationUser>> _users = IdentityTestFactory.UserManager();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IDbContextTransaction> _transaction = new();
    private readonly Mock<ILogger<AdminUserService>> _logger = new();
    private readonly ApplicationUser _target = new() { Id = 2, UserName = "target", Email = "target@example.com", EmailConfirmed = true };
    private AdminUserService Service => new(_repository.Object, _userRepository.Object, _users.Object, _refresh.Object, _logger.Object);

    public AdminUserServiceTests()
    {
        _repository.Setup(repository => repository.BeginRoleChangeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_transaction.Object);
        _userRepository.Setup(repository => repository.FindByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(_target);
        _users.Setup(users => users.GetRolesAsync(_target)).ReturnsAsync(new List<string> { AppRoles.User });
        _users.Setup(users => users.RemoveFromRoleAsync(_target, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(users => users.AddToRoleAsync(_target, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
    }

    [Fact]
    public async Task ChangeRole_WithDifferentValidRole_AuditsAndRevokesBeforeCommitting()
    {
        // Arrange
        RoleChangeLog? audit = null;
        _repository.Setup(repository => repository.AddRoleChangeAsync(It.IsAny<RoleChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<RoleChangeLog, CancellationToken>((change, _) => audit = change).Returns(Task.CompletedTask);

        // Act
        var result = await Service.ChangeRoleAsync(1, 2, AppRoles.Moderator);

        // Assert
        Assert.Null(result.Error);
        Assert.Equal(AppRoles.Moderator, result.User!.Role);
        Assert.NotNull(audit);
        Assert.Equal(1, audit.ChangedByUserId); Assert.Equal(2, audit.TargetUserId);
        Assert.Equal(AppRoles.User, audit.OldRole); Assert.Equal(AppRoles.Moderator, audit.NewRole);
        Assert.Equal(DateTimeKind.Utc, audit.ChangedAt.Kind);
        _users.Verify(users => users.RemoveFromRoleAsync(_target, AppRoles.User), Times.Once);
        _users.Verify(users => users.AddToRoleAsync(_target, AppRoles.Moderator), Times.Once);
        _refresh.Verify(repository => repository.RevokeAllActiveAsync(2, audit.ChangedAt, It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(transaction => transaction.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeRole_WithInvalidRole_RejectsBeforeTransaction()
    {
        // Arrange / Act
        var result = await Service.ChangeRoleAsync(1, 2, "unknown");
        // Assert
        Assert.Equal(RoleChangeError.InvalidRole, result.Error);
        _repository.Verify(repository => repository.BeginRoleChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
        AssertNoWrite();
    }

    [Fact]
    public async Task ChangeRole_WithActualSelfChange_ReturnsErrorWithoutWrites()
    {
        // Arrange / Act
        var result = await Service.ChangeRoleAsync(2, 2, AppRoles.Moderator);
        // Assert
        Assert.Equal(RoleChangeError.SelfRoleChange, result.Error);
        AssertNoWrite();
    }

    [Fact]
    public async Task ChangeRole_WithMissingTarget_ReturnsNotFoundWithoutWrites()
    {
        // Arrange / Act
        var result = await Service.ChangeRoleAsync(1, 999, AppRoles.User);
        // Assert
        Assert.Equal(RoleChangeError.NotFound, result.Error);
        AssertNoWrite();
    }

    [Fact]
    public async Task ChangeRole_WithSameRole_ReturnsUserWithoutAuditRevocationOrCommit()
    {
        // Arrange / Act
        var result = await Service.ChangeRoleAsync(1, 2, AppRoles.User);
        // Assert
        Assert.Null(result.Error); Assert.Equal(AppRoles.User, result.User!.Role);
        _users.Verify(users => users.RemoveFromRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        AssertNoWrite();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task ChangeRole_DemotingAdmin_ChecksAdminCountWithinTransaction(int adminCount, bool allowed)
    {
        // Arrange
        var order = new MockSequence();
        _repository.InSequence(order).Setup(repository => repository.BeginRoleChangeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_transaction.Object);
        _repository.InSequence(order).Setup(repository => repository.CountAdminsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(adminCount);
        _users.Setup(users => users.GetRolesAsync(_target)).ReturnsAsync(new List<string> { AppRoles.Admin });
        // Act
        var result = await Service.ChangeRoleAsync(1, 2, AppRoles.User);
        // Assert
        Assert.Equal(allowed ? null : RoleChangeError.LastAdmin, result.Error);
        _repository.Verify(repository => repository.CountAdminsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(transaction => transaction.CommitAsync(It.IsAny<CancellationToken>()), allowed ? Times.Once() : Times.Never());
        if (!allowed) AssertNoWrite();
    }

    [Fact]
    public async Task ChangeRole_WhenIdentityUpdateFails_DoesNotCommitAuditOrRevocation()
    {
        // Arrange
        _users.Setup(users => users.AddToRoleAsync(_target, AppRoles.Admin)).ReturnsAsync(IdentityResult.Failed(new IdentityError()));
        // Act
        var result = await Service.ChangeRoleAsync(1, 2, AppRoles.Admin);
        // Assert
        Assert.Equal(RoleChangeError.Unexpected, result.Error);
        AssertNoWrite();
    }

    [Fact]
    public async Task ChangeRole_WhenPersistenceThrows_DoesNotCommitAndLogsNoSensitiveExceptionText()
    {
        // Arrange
        _refresh.Setup(repository => repository.RevokeAllActiveAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive-token-value"));
        // Act
        var result = await Service.ChangeRoleAsync(1, 2, AppRoles.Admin);
        // Assert
        Assert.Equal(RoleChangeError.Unexpected, result.Error);
        _transaction.Verify(transaction => transaction.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(_logger.Invocations, call => string.Join(" ", call.Arguments).Contains("sensitive-token-value"));
    }

    private void AssertNoWrite()
    {
        _repository.Verify(repository => repository.AddRoleChangeAsync(It.IsAny<RoleChangeLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _refresh.Verify(repository => repository.RevokeAllActiveAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _transaction.Verify(transaction => transaction.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
