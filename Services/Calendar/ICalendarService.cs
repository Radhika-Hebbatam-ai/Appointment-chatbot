namespace AppointmentChatbot.Services;

public record TimeSlot(DateTime Start, DateTime End);

public record BookedEvent(
    string EventId,
    DateTime Start,
    DateTime End,
    string CustomerName,
    string CustomerEmail,
    string Description = "");

/// <summary>
/// Calendar provider abstraction — swap the implementation (Google, Outlook, Database)
/// without touching any agent/plugin code. Configured per business via appsettings.
/// </summary>
public interface ICalendarService
{
    /// <summary>
    /// Returns free 30-minute slots on the given date within business hours.
    /// </summary>
    Task<List<TimeSlot>> GetAvailableSlotsAsync(DateTime date);

    /// <summary>
    /// Checks if a specific date/time slot is free.
    /// </summary>
    Task<bool> IsSlotAvailableAsync(DateTime start, DateTime end);

    /// <summary>
    /// Creates a calendar event and returns the event ID.
    /// </summary>
    Task<string> CreateEventAsync(DateTime start, DateTime end, string customerName, string customerEmail, string providerEmail, string description);

    /// <summary>
    /// Moves an existing event to a new time.
    /// </summary>
    Task<BookedEvent> RescheduleEventAsync(string eventId, DateTime newStart, DateTime newEnd);

    /// <summary>
    /// Cancels an existing event.
    /// </summary>
    Task CancelEventAsync(string eventId);
    /// <summary>
    /// WHAT: Returns all booked events for a specific date.
    /// HOW:  Queries the calendar for actual events (not just free/busy),
    ///       returns a list of BookedEvent records for that day.
    ///       Used by the evening reminder service to list tomorrow's appointments.
    /// </summary>
    Task<List<BookedEvent>> GetEventsForDateAsync(DateTime date);
}