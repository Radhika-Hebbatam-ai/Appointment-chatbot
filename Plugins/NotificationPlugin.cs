using System.ComponentModel;
using Microsoft.SemanticKernel;
using AppointmentChatbot.Services;

namespace AppointmentChatbot.Plugins;

/// <summary>
/// WHAT: Notification Agent — sends the right email for each event type.
/// HOW:  Delegates entirely to IEmailService (ACS implementation).
///       Knows nothing about calendars, bookings, or availability —
///       its only job is formatting and sending the right email.
///       Single responsibility: notify both parties after any booking action.
/// </summary>
public class NotificationPlugin
{
    private readonly IEmailService _emailService;

    public NotificationPlugin(IEmailService emailService)
    {
        _emailService = emailService;
    }

    [KernelFunction("send_confirmation_email")]
    [Description("Sends a booking confirmation email to both the customer and provider. " +
                 "Always call this immediately after a successful book_appointment.")]
    public async Task<string> SendConfirmationEmailAsync(
        [Description("Customer's email address")]
        string customerEmail,
        [Description("Customer's full name")]
        string customerName,
        [Description("Provider's email address")]
        string providerEmail,
        [Description("Appointment start time in ISO format e.g. 2026-07-10T14:00:00")]
        string startIso,
        [Description("Appointment end time in ISO format e.g. 2026-07-10T14:30:00")]
        string endIso)
    {
        if (!DateTime.TryParse(startIso, out var start) ||
            !DateTime.TryParse(endIso, out var end))
            return "EMAIL_FAILED: could not parse start/end time. Use ISO format.";

        await _emailService.SendAppointmentConfirmationAsync(
            customerEmail, customerName, providerEmail, start, end);

        return "CONFIRMATION_EMAIL_SENT: both customer and provider notified.";
    }

    [KernelFunction("send_reschedule_email")]
    [Description("Sends a reschedule notice to both customer and provider. " +
                 "Always call this immediately after a successful reschedule_appointment.")]
    public async Task<string> SendRescheduleEmailAsync(
        [Description("Customer's email address")]
        string customerEmail,
        [Description("Customer's full name")]
        string customerName,
        [Description("Provider's email address")]
        string providerEmail,
        [Description("Original appointment time in ISO format")]
        string oldStartIso,
        [Description("New appointment start time in ISO format")]
        string newStartIso,
        [Description("New appointment end time in ISO format")]
        string newEndIso)
    {
        if (!DateTime.TryParse(oldStartIso, out var oldStart) ||
            !DateTime.TryParse(newStartIso, out var newStart) ||
            !DateTime.TryParse(newEndIso, out var newEnd))
            return "EMAIL_FAILED: could not parse times. Use ISO format.";

        await _emailService.SendRescheduleNoticeAsync(
            customerEmail, customerName, providerEmail, oldStart, newStart, newEnd);

        return "RESCHEDULE_EMAIL_SENT: both customer and provider notified.";
    }

    [KernelFunction("send_cancellation_email")]
    [Description("Sends a cancellation notice to both customer and provider.")]
    public async Task<string> SendCancellationEmailAsync(
        [Description("Customer's email address")]
        string customerEmail,
        [Description("Customer's full name")]
        string customerName,
        [Description("Provider's email address")]
        string providerEmail,
        [Description("Cancelled appointment start time in ISO format")]
        string startIso)
    {
        if (!DateTime.TryParse(startIso, out var start))
            return "EMAIL_FAILED: could not parse start time. Use ISO format.";

        await _emailService.SendCancellationNoticeAsync(
            customerEmail, customerName, providerEmail, start);

        return "CANCELLATION_EMAIL_SENT: both customer and provider notified.";
    }
}