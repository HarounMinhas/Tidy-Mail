namespace MailboxCleaner.Web.Application.DTOs;

public sealed record MailItemDto(
    string Id,
    string SenderEmail,
    string SenderName,
    string Domain,
    string Subject,
    DateTimeOffset? ReceivedAt,
    bool IsRead);
