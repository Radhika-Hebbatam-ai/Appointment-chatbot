namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: Contract for sending appointment reminders to the business owner.
/// HOW:  Implementations check tomorrow's calendar events and send
///       a summary email with full details and prerequisites.
/// </summary>
public interface IReminderService
{
    /// <summary>
    /// WHAT: Sends evening reminder to business owner with tomorrow's appointments.
    /// HOW:  Fetches tomorrow's events from calendar, looks up prerequisites
    ///       for each service via RAG, builds and sends a summary email.
    /// </summary>
    Task SendEveningReminderAsync();
}