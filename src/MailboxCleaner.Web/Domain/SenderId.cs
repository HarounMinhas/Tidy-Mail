namespace MailboxCleaner.Web.Domain;

public sealed record SenderId(string Email)
{
    public string Domain => Email.Split('@').LastOrDefault() ?? string.Empty;
}
