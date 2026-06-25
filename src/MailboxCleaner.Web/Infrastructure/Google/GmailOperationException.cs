namespace MailboxCleaner.Web.Infrastructure.Google;

public sealed class GmailOperationException : Exception
{
    public GmailOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
