namespace AppointmentChatbot.Services;

public record TimeSlot(DateTime Start, DateTime End);

public record BookedEvent(string EventId, DateTime Start, DateTime End, string CustomerName, string CustomerEmail);

public interface IGoogleCalendarService
{
    /// <summary>
    /// Returns free slots on the given date within business hours (9am-5pm, 30 min slots).
    /// </summary>
    Task<List<TimeSlot>> GetAvailableSlotsAsync(DateTime date);

    /// <summary>
    /// Checks if a specific date/time is free. Returns true if bookable.
    /// </summary>
    Task<bool> IsSlotAvailableAsync(DateTime start, DateTime end);

    /// <summary>
    /// Creates the calendar event and returns the event ID (needed later for reschedule/cancel).
    /// </summary>
    Task<string> CreateEventAsync(DateTime start, DateTime end, string customerName, string customerEmail, string providerEmail, string description);

    /// <summary>
    /// Moves an existing event to a new time. Returns the updated event.
    /// </summary>
    Task<BookedEvent> RescheduleEventAsync(string eventId, DateTime newStart, DateTime newEnd);

    /// <summary>
    /// Cancels/deletes an existing event.
    /// </summary>
    Task CancelEventAsync(string eventId);
}