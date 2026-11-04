namespace Playtrough.Api.Services.Contracts;

/// <summary>Expected review failure, translated to ProblemDetails by the web layer.</summary>
public sealed class ReviewException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
