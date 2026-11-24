namespace Playtrough.Api.Models;

public sealed class ChatSettings
{
    public int MessagesPerUser { get; set; } = 10;
    public int GlobalMessages { get; set; } = 200;
    public int WindowSeconds { get; set; } = 10;
}
