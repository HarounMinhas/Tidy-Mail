using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using MailboxCleaner.Web.Infrastructure.Google;
using MailboxCleaner.Web.Infrastructure.Google.Gmail;
using System.Net;

namespace MailboxCleaner.Web.Tests;

public sealed class GmailMetadataRequestTests
{
    [Fact]
    public void RequestUsesMetadataFormatAndOnlyNecessaryFields()
    {
        using var service = new GmailService(new BaseClientService.Initializer { ApplicationName = "Test" });
        var request = GmailClient.BuildMetadataRequest(service, "synthetic-id");
        Assert.Equal(UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata, request.Format);
        Assert.Equal("id,threadId,labelIds,sizeEstimate,payload(headers(name,value))", request.Fields);
        Assert.Contains("From", request.MetadataHeaders);
        Assert.Contains("List-Unsubscribe", request.MetadataHeaders);
        Assert.DoesNotContain("body", request.Fields);
        Assert.DoesNotContain("parts", request.Fields);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    public void ClassifiesErrorsWithoutExposingProviderDetails(int status, bool retryable)
    {
        var error = new GoogleApiException("gmail", "secret-token private@example.com")
        {
            HttpStatusCode = (HttpStatusCode)status
        };
        Assert.Equal(retryable, GmailClient.IsTransient(error));
        var diagnostic = GmailScanDiagnostics.Describe(error);
        Assert.Contains($"HTTP {status}", diagnostic);
        Assert.DoesNotContain("secret-token", diagnostic);
        Assert.DoesNotContain("private@example.com", diagnostic);
    }
}
