using System.ComponentModel;
using Microsoft.SemanticKernel;
using AppointmentChatbot.Services;

namespace AppointmentChatbot.Plugins;

/// <summary>
/// WHAT: Rescheduling Agent — finds an existing booking and moves it to a new time.
/// HOW:  Looks up the existing booking by customer email from IBookingStore
///       to get the eventId, verifies the new slot is free, then calls
///       ICalendarService to move the event.
///       Single responsibility: move an existing booking, nothing else.
///       Separate from BookingPlugin because the logic is different —
///       find + validate ownership + update vs create brand new.
/// </summary>
public class ReschedulingPlugin
{
    private readonly ICalendarService _calendar;
    private readonly IBookingStore _bookingStore;

    public ReschedulingPlugin(
        ICalendarService calendar,
        IBookingStore bookingStore)
    {
        _calendar = calendar;
        _bookingStore = bookingStore;
    }

    [KernelFunction("reschedule_appointment")]
    [Description("Reschedules an existing appointment to a new date and time. " +
                 "Requires the customer's email to find their existing booking. " +
                 "Always check the new slot is available before calling this.")]
    public async Task<string> RescheduleAppointmentAsync(
        [Description("Customer's email address — used to find their existing booking")]
        string customerEmail,
        [Description("New date in yyyy-MM-dd format e.g. 2026-07-10")]
        string newDate,
        [Description("New time in HH:mm 24-hour format e.g. 14:00")]
        string newTime)
    {
        // Find existing booking by customer email
        var existing = _bookingStore.FindByCustomerEmail(customerEmail);
        if (existing is null)
            return "RESCHEDULE_FAILED: no existing booking found for that email. " +
                   "Please ask the customer to confirm the email they booked with.";

        if (!DateTime.TryParse($"{newDate} {newTime}", out var newStart))
            return "RESCHEDULE_FAILED: could not parse new date/time. " +
                   "Use yyyy-MM-dd and HH:mm format.";

        var newEnd = newStart.AddMinutes(30);

        // Verify new slot is available before moving
        var isAvailable = await _calendar.IsSlotAvailableAsync(newStart, newEnd);
        if (!isAvailable)
            return "RESCHEDULE_FAILED: the new slot is not available. " +
                   "Please run check_availability to find a free slot first.";

        var oldStart = existing.Start;

        // Move the calendar event
        var updated = await _calendar.RescheduleEventAsync(
            existing.EventId, newStart, newEnd);

        // Update booking store with new times
        _bookingStore.Save(existing with { Start = newStart, End = newEnd });

        return $"RESCHEDULE_CONFIRMED: eventId={updated.EventId}, " +
               $"oldTime={oldStart:yyyy-MM-dd HH:mm}, " +
               $"newTime={newStart:yyyy-MM-dd HH:mm}-{newEnd:HH:mm}, " +
               $"customerEmail={existing.CustomerEmail}, " +
               $"providerEmail={existing.ProviderEmail}. " +
               $"Now call send_reschedule_email to notify both parties.";
    }
}