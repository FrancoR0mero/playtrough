namespace Playtrough.Api.Services.Contracts;

/// <summary>Expected library failure, translated to ProblemDetails by the web layer.</summary>
public sealed class LibraryException(string code, DateOnly? minDate = null) : Exception(code)
{
    public string Code { get; } = code;
    public DateOnly? MinDate { get; } = minDate;
}
