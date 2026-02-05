namespace MailboxCleaner.Web.Application.DTOs;

public sealed record SenderStatDto(string Email, string Name, int Count, string Domain);
