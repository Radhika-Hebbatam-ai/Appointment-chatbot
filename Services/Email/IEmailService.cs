namespace AppointmentChatbot.Services;

public interface IEmailService
{
    Task SendAppointmentConfirmationAsync(string customerEmail, string customerName,
        string providerEmail, DateTime start, DateTime end);

    Task SendRescheduleNoticeAsync(string customerEmail, string customerName,
        string providerEmail, DateTime oldStart, DateTime newStart, DateTime newEnd);

    Task SendCancellationNoticeAsync(string customerEmail, string customerName,
        string providerEmail, DateTime start);
    /// <summary>
    /// WHAT: Sends the evening appointment reminder to the business owner.
    /// HOW:  Plain-text email with tomorrow's appointments, times, and prerequisites.
    ///       Only sent to the owner — not to customers.
    /// </summary>
    Task SendEveningReminderAsync(string ownerEmail, string subject, string body);
}