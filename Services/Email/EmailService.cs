using Azure;
using Azure.Communication.Email;

namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: Sends appointment emails via Azure Communication Services Email.
/// HOW:  Uses ACS Email SDK with the business owner's connection string.
///       Emails are sent FROM the business owner's verified sender address
///       so customers see the business name, not a third-party service.
/// </summary>
public class AzureCommunicationEmailService : IEmailService
{
    private readonly EmailClient _emailClient;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public AzureCommunicationEmailService(IConfiguration config)
    {
        var connectionString = config["BusinessConfig:AcsConnectionString"]
            ?? throw new InvalidOperationException(
                "BusinessConfig:AcsConnectionString not configured");

        _fromEmail = config["BusinessConfig:BookingEmail"]
            ?? throw new InvalidOperationException(
                "BusinessConfig:BookingEmail not configured");

        _fromName = config["BusinessConfig:BookingEmailName"]
            ?? config["BusinessConfig:Name"]
            ?? "Appointment Bot";

        // ACS EmailClient authenticated via connection string
        _emailClient = new EmailClient(connectionString);
    }

    /// <summary>
    /// WHAT: Sends booking confirmation to both customer and provider.
    /// HOW:  Builds plain-text email body and sends to both addresses via ACS.
    ///       Called immediately after a booking is confirmed.
    /// </summary>
    public async Task SendAppointmentConfirmationAsync(
        string customerEmail, string customerName,
        string providerEmail, DateTime start, DateTime end)
    {
        var subject = "Appointment Confirmed";
        var body = $"""
            Hi {customerName},

            Your appointment is confirmed:

            Date:  {start:dddd, d MMMM yyyy}
            Time:  {start:h:mm tt} - {end:h:mm tt}

            You will receive a reminder the evening before your appointment.
            Reply to this email if you need to make any changes.

            Thanks,
            {_fromName}
            """;

        await SendToBothAsync(customerEmail, customerName, providerEmail, subject, body);
    }

    /// <summary>
    /// WHAT: Sends reschedule notice to both customer and provider.
    /// HOW:  Includes both old and new times so both parties are clear on
    ///       what changed. Called immediately after reschedule is confirmed.
    /// </summary>
    public async Task SendRescheduleNoticeAsync(
        string customerEmail, string customerName,
        string providerEmail, DateTime oldStart,
        DateTime newStart, DateTime newEnd)
    {
        var subject = "Appointment Rescheduled";
        var body = $"""
            Hi {customerName},

            Your appointment has been rescheduled:

            Previous time: {oldStart:dddd, d MMMM yyyy h:mm tt}
            New time:      {newStart:dddd, d MMMM yyyy} at {newStart:h:mm tt} - {newEnd:h:mm tt}

            Reply to this email if you have any questions.

            Thanks,
            {_fromName}
            """;

        await SendToBothAsync(customerEmail, customerName, providerEmail, subject, body);
    }

    /// <summary>
    /// WHAT: Sends cancellation notice to both customer and provider.
    /// HOW:  Simple notification with the cancelled appointment time.
    ///       Called immediately after cancellation is confirmed.
    /// </summary>
    public async Task SendCancellationNoticeAsync(
        string customerEmail, string customerName,
        string providerEmail, DateTime start)
    {
        var subject = "Appointment Cancelled";
        var body = $"""
            Hi {customerName},

            Your appointment on {start:dddd, d MMMM yyyy} at {start:h:mm tt}
            has been cancelled.

            Reply to this email if you would like to rebook.

            Thanks,
            {_fromName}
            """;

        await SendToBothAsync(customerEmail, customerName, providerEmail, subject, body);
    }

    /// <summary>
    /// WHAT: Sends email to both customer and business provider.
    /// HOW:  ACS SendAsync fires two separate sends — one per recipient.
    ///       WaitUntil.Completed ensures delivery result is returned
    ///       before continuing, so failures surface immediately.
    /// </summary>
    private async Task SendToBothAsync(
        string customerEmail, string customerName,
        string providerEmail, string subject, string body)
    {
        var recipients = new[]
        {
            (Email: customerEmail, Name: customerName),
            (Email: providerEmail, Name: _fromName)
        };

        foreach (var recipient in recipients)
        {
            var message = new EmailMessage(
                senderAddress: _fromEmail,
                recipientAddress: recipient.Email,
                content: new EmailContent(subject)
                {
                    PlainText = body
                });

            var operation = await _emailClient.SendAsync(
                WaitUntil.Completed, message);

            if (operation.Value.Status == EmailSendStatus.Failed)
                throw new InvalidOperationException(
                    $"ACS email failed to {recipient.Email}: {operation.Value.Status}");
        }
    }
    /// <summary>
    /// WHAT: Sends the evening appointment reminder to the business owner only.
    /// HOW:  Single recipient (owner) — not sent to customers.
    ///       Subject and body are pre-built by AppointmentReminderService.
    /// </summary>
    public async Task SendEveningReminderAsync(
        string ownerEmail, string subject, string body)
    {
        var message = new EmailMessage(
            senderAddress: _fromEmail,
            recipientAddress: ownerEmail,
            content: new EmailContent(subject)
            {
                PlainText = body
            });

        var operation = await _emailClient.SendAsync(
            WaitUntil.Completed, message);

        if (operation.Value.Status == EmailSendStatus.Failed)
            throw new InvalidOperationException(
                $"ACS reminder email failed: {operation.Value.Status}");
    }
}