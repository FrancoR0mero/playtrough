namespace Playtrough.Api.Models;

public sealed class GroupSettings
{
    public const string SectionName = "Groups";

    public string[] Topics { get; set; } = [];

    internal bool HasValidTopics() => Topics is { Length: > 0 }
        && Topics.All(topic => topic is { Length: > 0 and <= SocialGroup.MaxTopicLength }
            && topic == topic.Trim() && !topic.Any(char.IsControl))
        && Topics.Distinct(StringComparer.OrdinalIgnoreCase).Count() == Topics.Length;
}
