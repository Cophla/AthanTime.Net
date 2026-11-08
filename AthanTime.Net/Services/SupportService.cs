using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace AthanTime.Net.Services;

/// <summary>Identifies the user's email action.</summary>
public enum SupportMessage { DebugLogs, Suggestion, Complaint }

/// <summary>Creates localized drafts to the configured project mailbox; never connects to a mail server.</summary>
public sealed class SupportService(ILocalizationService localization, IConfiguration configuration)
{
    private readonly ILocalizationService _localization = localization;
    // Retain the public support destination when a custom configuration omits the setting.
    private readonly string _recipient = configuration["Application:SupportEmail"] ?? "athantimedotnet@gmail.com";

    /// <summary>Creates a mailto draft. Attachments must be added by the user in their mail app.</summary>
    public Uri CreateDraft(SupportMessage message, string language, string? attachmentName = null)
    {
        var subjectKey = message switch
        {
            SupportMessage.DebugLogs => "EmailLogs",
            SupportMessage.Suggestion => "Suggestion",
            _ => "Complaint"
        };
        var subject = $"Athan Time — {_localization.Get(language, subjectKey)}";
        var version = App.ReleaseVersion;
        var body = $"Athan Time {version}\n{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n" +
            $".NET {Environment.Version}\n\n{_localization.Get(language, "EmailBody")}";
        if (attachmentName is not null)
            body += "\n\n" + string.Format(CultureInfo.CurrentCulture,
                _localization.Get(language, "AttachLogs"), attachmentName);
        // Escape every field so arbitrary localized text cannot introduce mail headers.
        var recipient = new System.Net.Mail.MailAddress(_recipient).Address;
        return new Uri($"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
    }
}
