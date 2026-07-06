using System.ComponentModel;
using Microsoft.SemanticKernel;
using AppointmentChatbot.Services;
using AppointmentChatbot.Services.Staff;

namespace AppointmentChatbot.Plugins;

/// <summary>
/// WHAT: Booking Agent — creates a confirmed calendar appointment.
/// HOW:  Calls ICalendarService to create the event and IBookingStore
///       to save the booking for later reschedule/cancel lookup.
///       Double-checks availability before booking as a race condition guard
///       (slot could get taken between availability check and confirmation).
///       Single responsibility: create a confirmed booking, nothing else.
/// </summary>
public class BookingPlugin
{
    private readonly ICalendarService _calendar;
    private readonly IBookingStore _bookingStore;
    private readonly IStaffStore _staffStore;

    public BookingPlugin(
        ICalendarService calendar,
        IBookingStore bookingStore,
        IStaffStore staffStore)
    {
        _calendar = calendar;
        _bookingStore = bookingStore;
        _staffStore = staffStore;
    }

    [KernelFunction("book_appointment")]
    [Description("Books a confirmed appointment. Only call this after check_availability " +
                 "confirmed the slot is free AND the customer has explicitly confirmed " +
                 "they want to proceed. Requires customer name, email, provider name or " +
                 "email, date, time, and reason for the appointment.")]
    public async Task<string> BookAppointmentAsync(
        [Description("Date in yyyy-MM-dd format e.g. 2026-07-10")]
        string date,
        [Description("Time in HH:mm 24-hour format e.g. 14:00")]
        string time,
        [Description("Customer's full name")]
        string customerName,
        [Description("Customer's email address")]
        string customerEmail,
        [Description("Provider's name (e.g. 'Dr Smith') or email directly")]
        string providerNameOrEmail,
        [Description("Short reason or service type for the appointment")]
        string reason)
    {
        if (!DateTime.TryParse($"{date} {time}", out var start))
            return "BOOKING_FAILED: could not parse date/time. " +
                   "Use yyyy-MM-dd and HH:mm format.";

        var end = start.AddMinutes(30);

        // Race condition guard — re-check availability right before booking
        var stillFree = await _calendar.IsSlotAvailableAsync(start, end);
        if (!stillFree)
            return "BOOKING_FAILED: that slot was just taken. " +
                   "Please run check_availability again to find a free slot.";

        // Resolve provider email — try staff store first, fall back to direct email
        var providerEmail = _staffStore.GetEmailByName(providerNameOrEmail)
            ?? providerNameOrEmail;

        // Create the calendar event
        var eventId = await _calendar.CreateEventAsync(
            start, end, customerName, customerEmail, providerEmail, reason);

        // Save to booking store so reschedule/cancel can find it by customer email
        _bookingStore.Save(new Booking(
            eventId, customerName, customerEmail, providerEmail, start, end));

        return $"BOOKING_CONFIRMED: eventId={eventId}, " +
               $"date={start:yyyy-MM-dd}, " +
               $"time={start:HH:mm}-{end:HH:mm}, " +
               $"customer={customerEmail}, " +
               $"provider={providerEmail}. " +
               $"Now call send_confirmation_email to notify both parties.";
    }
}