namespace MailboxCleaner.Web.Application.Sorting;

public enum SenderSortOption
{
    CountDesc,
    CountAsc,
    SenderNameAsc,
    SenderNameDesc,
    EmailAsc,
    EmailDesc,
    NewestMessageAsc,
    NewestMessageDesc,
    OldestMessageAsc,
    OldestMessageDesc,
    SenderAsc = EmailAsc,
    SenderDesc = EmailDesc
}
