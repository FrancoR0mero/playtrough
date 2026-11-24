namespace Playtrough.Api.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Register = "register";
    public const string PasswordRecovery = "password-recovery";
    public const string TokenActions = "token-actions";
    public const string Refresh = "refresh";
    public const string Catalog = "catalog";
    public const string UserSearch = "user-search";
    public const string FriendsWrite = "friends-write";
    public const string ProfileWrite = "profile-write";
    public const string ReviewsWrite = "reviews-write";
    public const string TimelineRead = "timeline-read";
    public const string TimelineWrite = "timeline-write";
    public const string ReactionsWrite = "reactions-write";
    public const string CommentsWrite = "comments-write";
    public const string Global = "global";
}
