using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Options;

namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: Microsoft Outlook/365 calendar implementation of ICalendarService.
/// HOW:  Uses Microsoft Graph API via the official Graph SDK.
///       Authenticates via Azure AD app registration (client credentials flow)
///       — no user login required, app acts on behalf of the configured UserId.
///       Swap from Google to Outlook by changing CalendarProvider in appsettings.json.
/// </summary>
public class OutlookCalendarService : ICalendarService
{
    private readonly GraphServiceClient _graphClient;
    private readonly string _userId;
    private static readonly TimeSpan BusinessStart = TimeSpan.FromHours(9);
    private static readonly TimeSpan BusinessEnd = TimeSpan.FromHours(17);
    private static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(30);

    public OutlookCalendarService(IConfiguration config)
    {
        var tenantId = config["OutlookCalendar:TenantId"]
            ?? throw new InvalidOperationException("OutlookCalendar:TenantId not configured");
        var clientId = config["OutlookCalendar:ClientId"]
            ?? throw new InvalidOperationException("OutlookCalendar:ClientId not configured");
        var clientSecret = config["OutlookCalendar:ClientSecret"]
            ?? throw new InvalidOperationException("OutlookCalendar:ClientSecret not configured");

        _userId = config["OutlookCalendar:UserId"]
            ?? throw new InvalidOperationException("OutlookCalendar:UserId not configured");

        // ClientSecretCredential authenticates as the app (not a user)
        // using the Azure AD app registration credentials
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(credential);
    }

    /// <summary>
    /// WHAT: Returns free 30-minute slots on a given date within business hours.
    /// HOW:  Calls Graph API getSchedule to get busy periods, then walks
    ///       every 30-min slot in business hours and returns non-overlapping ones.
    /// </summary>
    public async Task<List<TimeSlot>> GetAvailableSlotsAsync(DateTime date)
    {
        var dayStart = date.Date + BusinessStart;
        var dayEnd = date.Date + BusinessEnd;

        var busyTimes = await GetBusyTimesAsync(dayStart, dayEnd);

        var freeSlots = new List<TimeSlot>();
        var cursor = dayStart;

        while (cursor + SlotLength <= dayEnd)
        {
            var slotEnd = cursor + SlotLength;
            bool overlapsBusy = busyTimes.Any(b => cursor < b.End && slotEnd > b.Start);

            if (!overlapsBusy)
                freeSlots.Add(new TimeSlot(cursor, slotEnd));

            cursor = slotEnd;
        }

        return freeSlots;
    }

    /// <summary>
    /// WHAT: Checks if a specific slot is free.
    /// HOW:  Gets busy periods for the slot's time range and checks for overlap.
    /// </summary>
    public async Task<bool> IsSlotAvailableAsync(DateTime start, DateTime end)
    {
        var busyTimes = await GetBusyTimesAsync(start, end);
        return !busyTimes.Any(b => start < b.End && end > b.Start);
    }

    /// <summary>
    /// WHAT: Creates a calendar event in the owner's Outlook calendar.
    /// HOW:  POST /users/{userId}/events via Microsoft Graph.
    ///       Adds customer and provider as attendees — both get Outlook invites.
    /// </summary>
    public async Task<string> CreateEventAsync(
        DateTime start, DateTime end,
        string customerName, string customerEmail,
        string providerEmail, string description)
    {
        var newEvent = new Event
        {
            Subject = $"Appointment: {customerName}",
            Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = description
            },
            Start = new DateTimeTimeZone
            {
                DateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = "New Zealand Standard Time"
            },
            End = new DateTimeTimeZone
            {
                DateTime = end.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = "New Zealand Standard Time"
            },
            Attendees = new List<Attendee>
            {
                new() {
                    EmailAddress = new EmailAddress
                    {
                        Address = customerEmail,
                        Name = customerName
                    },
                    Type = AttendeeType.Required
                },
                new() {
                    EmailAddress = new EmailAddress
                    {
                        Address = providerEmail
                    },
                    Type = AttendeeType.Required
                }
            }
        };

        var created = await _graphClient.Users[_userId].Events
            .PostAsync(newEvent);

        return created?.Id
            ?? throw new InvalidOperationException("Outlook event creation returned null ID");
    }

    /// <summary>
    /// WHAT: Moves an existing event to a new time.
    /// HOW:  PATCH /users/{userId}/events/{eventId} with updated start/end times.
    ///       Microsoft Graph sends update notifications to all attendees automatically.
    /// </summary>
    public async Task<BookedEvent> RescheduleEventAsync(
        string eventId, DateTime newStart, DateTime newEnd)
    {
        var update = new Event
        {
            Start = new DateTimeTimeZone
            {
                DateTime = newStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = "New Zealand Standard Time"
            },
            End = new DateTimeTimeZone
            {
                DateTime = newEnd.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = "New Zealand Standard Time"
            }
        };

        var updated = await _graphClient.Users[_userId].Events[eventId]
            .PatchAsync(update);

        var customerAttendee = updated?.Attendees?.FirstOrDefault();

        return new BookedEvent(
            updated?.Id ?? eventId,
            newStart,
            newEnd,
            customerAttendee?.EmailAddress?.Name ?? "Customer",
            customerAttendee?.EmailAddress?.Address ?? string.Empty);
    }

    /// <summary>
    /// WHAT: Cancels an existing event.
    /// HOW:  DELETE /users/{userId}/events/{eventId} via Microsoft Graph.
    ///       Graph automatically sends cancellation notices to all attendees.
    /// </summary>
    public async Task CancelEventAsync(string eventId)
    {
        await _graphClient.Users[_userId].Events[eventId].DeleteAsync();
    }

    /// <summary>
    /// WHAT: Returns all booked events for a specific date.
    /// HOW:  GET /users/{userId}/calendarView with start/end date range filter.
    ///       Returns events ordered by start time.
    /// </summary>
    public async Task<List<BookedEvent>> GetEventsForDateAsync(DateTime date)
    {
        var dayStart = date.Date;
        var dayEnd = date.Date.AddDays(1);

        var events = await _graphClient.Users[_userId].CalendarView
            .GetAsync(config =>
            {
                config.QueryParameters.StartDateTime =
                    dayStart.ToString("yyyy-MM-ddTHH:mm:ss");
                config.QueryParameters.EndDateTime =
                    dayEnd.ToString("yyyy-MM-ddTHH:mm:ss");
            });

        return events?.Value?
            .Select(e =>
            {
                var customerAttendee = e.Attendees?.FirstOrDefault();
                return new BookedEvent(
                    e.Id ?? string.Empty,
                    DateTime.Parse(e.Start!.DateTime!),
                    DateTime.Parse(e.End!.DateTime!),
                    customerAttendee?.EmailAddress?.Name ?? "Customer",
                    customerAttendee?.EmailAddress?.Address ?? string.Empty,
                    e.Body?.Content ?? string.Empty);
            })
            .ToList() ?? new List<BookedEvent>();
    }

    /// <summary>
    /// WHAT: Returns busy time ranges for a given period.
    /// HOW:  POST /users/{userId}/calendar/getSchedule via Graph API.
    ///       Returns free/busy blocks without exposing event details.
    /// </summary>
    private async Task<List<TimeSlot>> GetBusyTimesAsync(
        DateTime rangeStart, DateTime rangeEnd)
    {
        var requestBody = new Microsoft.Graph.Users.Item.Calendar
            .GetSchedule.GetSchedulePostRequestBody
        {
            Schedules = new List<string> { _userId },
            StartTime = new DateTimeTimeZone
            {
                DateTime = rangeStart.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = "New Zealand Standard Time"
            },
            EndTime = new DateTimeTimeZone
            {
                DateTime = rangeEnd.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = "New Zealand Standard Time"
            },
            AvailabilityViewInterval = 30
        };

        var response = await _graphClient.Users[_userId].Calendar
     .GetSchedule.PostAsGetSchedulePostResponseAsync(requestBody);

        return response?.Value?
            .SelectMany(s => s.ScheduleItems ?? new List<ScheduleItem>())
            .Where(s => s.Status == FreeBusyStatus.Busy)
            .Select(s => new TimeSlot(
                DateTime.Parse(s.Start!.DateTime!),
                DateTime.Parse(s.End!.DateTime!)))
            .ToList() ?? new List<TimeSlot>();
    }
}