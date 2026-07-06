using System.ComponentModel;
using Microsoft.SemanticKernel;
using AppointmentChatbot.Services;
using AppointmentChatbot.Services.Staff;

namespace AppointmentChatbot.Plugins;

/// <summary>
/// WHAT: Availability Agent — checks calendar and suggests free slots.
/// HOW:  Calls ICalendarService (Google or Outlook, swappable via config)
///       to check if a requested slot is free. If not, returns up to 3
///       nearest alternatives on the same day.
///       Single responsibility: answer "is this slot free, and if not, what is?"
/// </summary>
public class AvailabilityPlugin
{
    private readonly ICalendarService _calendar;

    public AvailabilityPlugin(ICalendarService calendar)
    {
        _calendar = calendar;
    }

    [KernelFunction("check_availability")]
    [Description("Checks if a specific date and time is available for booking. " +
                 "If not available, returns up to 3 alternative slots on the same day. " +
                 "Always call this before book_appointment.")]
    public async Task<string> CheckAvailabilityAsync(
        [Description("Requested date in yyyy-MM-dd format e.g. 2026-07-10")]
        string date,
        [Description("Requested time in HH:mm 24-hour format e.g. 14:00")]
        string time)
    {
        // Parse the requested date/time — return friendly error if unparseable
        if (!DateTime.TryParse($"{date} {time}", out var requestedStart))
            return "Could not understand that date/time. " +
                   "Please use yyyy-MM-dd for date and HH:mm for time e.g. 2026-07-10 14:00";

        var requestedEnd = requestedStart.AddMinutes(30);

        // Check if the requested slot is free
        var isFree = await _calendar.IsSlotAvailableAsync(requestedStart, requestedEnd);

        if (isFree)
            return $"AVAILABLE: {requestedStart:yyyy-MM-dd HH:mm} is free.";

        // Slot is taken — find nearest alternatives on the same day
        var daySlots = await _calendar.GetAvailableSlotsAsync(requestedStart.Date);

        var alternatives = daySlots
            .Where(s => s.Start != requestedStart)
            .Take(3)
            .Select(s => s.Start.ToString("yyyy-MM-dd HH:mm"))
            .ToList();

        if (!alternatives.Any())
            return $"NOT AVAILABLE: {requestedStart:yyyy-MM-dd HH:mm} is booked " +
                   $"and there are no other free slots that day. " +
                   $"Please suggest a different date.";

        return $"NOT AVAILABLE: {requestedStart:yyyy-MM-dd HH:mm} is booked. " +
               $"Nearest available slots: {string.Join(", ", alternatives)}.";
    }
}