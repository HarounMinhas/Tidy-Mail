namespace MailboxCleaner.Web.Domain;

public sealed class SenderStat
{
    public SenderStat(SenderId senderId, string displayName, int count)
    {
        SenderId = senderId;
        DisplayName = displayName;
        Count = count;
    }

    public SenderId SenderId { get; }
    public string DisplayName { get; }
    public int Count { get; }
}
