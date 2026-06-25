using MailboxCleaner.Web.Application.Services;
using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.Filtering;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.MailboxStats;
using MailboxCleaner.Web.Infrastructure.Google;
using MailboxCleaner.Web.Infrastructure.Security;
using MailboxCleaner.Web.Infrastructure.Google.Gmail;
using MailboxCleaner.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GoogleOAuthOptions>(builder.Configuration.GetSection("Google"));

builder.Services.AddDataProtection();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
    });

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(1);
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

builder.Services.AddScoped<ITokenStore, SessionTokenStore>();
builder.Services.AddScoped<IGoogleOAuthService, GoogleOAuthService>();
builder.Services.AddScoped<IGmailCredentialFactory, GoogleUserCredentialFactory>();
builder.Services.AddScoped<IGmailClient, GmailClient>();
builder.Services.AddSingleton<IMailboxMetadataStore, MailboxMetadataStore>();
builder.Services.AddScoped<MailboxScanService>();
builder.Services.AddScoped<MailboxStatsService>();
builder.Services.AddScoped<CleanupSuggestionService>();
builder.Services.AddScoped<MailboxFilterService>();
builder.Services.AddScoped<BulkActionPreviewService>();
builder.Services.AddScoped<GmailBulkActionService>();
builder.Services.AddScoped<ISenderAggregationService, SenderAggregationService>();
builder.Services.AddScoped<ISenderOverviewService, SenderOverviewService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
