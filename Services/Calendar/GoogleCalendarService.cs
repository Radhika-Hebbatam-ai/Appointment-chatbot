using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace AppointmentChatbot.Services;

public class GoogleCalendarService : ICalendarService
{
    private readonly CalendarService _calendarService;
    private readonly string _calendarId;
    private static readonly TimeSpan BusinessStart = TimeSpan.FromHours(9);
    private static readonly TimeSpan BusinessEnd = TimeSpan.FromHours(17);
    private static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(30);

    public GoogleCalendarService(IConfiguration config)
    {
        var keyPath = config["GoogleCalendar:ServiceAccountKeyPath"]
            ?? throw new InvalidOperationException("GoogleCalendar:ServiceAccountKeyPath not configured");
        _calendarId = config["GoogleCalendar:CalendarId"] ?? "primary";

        var credential = GoogleCredential
            .FromFile(keyPath)
            .CreateScoped(CalendarService.Scope.Calendar);

        _calendarService = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AppointmentChatbot"
        });
    }

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

    public async Task<bool> IsSlotAvailableAsync(DateTime start, DateTime end)
    {
        var busyTimes = await GetBusyTimesAsync(start, end);
        return !busyTimes.Any(b => start < b.End && end > b.Start);
    }

    public async Task<string> CreateEventAsync(DateTime start, DateTime end, string customerName, string customerEmail, string providerEmail, string description)
    {
        var newEvent = new Event
        {
            Summary = $"Appointment: {customerName}",
            Description = description,
            Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = "Pacific/Auckland" },
            End = new EventDateTime { DateTimeDateTimeOffset = end, TimeZone = "Pacific/Auckland" },
            Attendees = new List<EventAttendee>
            {
                new() { Email = customerEmail, DisplayName = customerName },
                new() { Email = providerEmail }
            }
        };

        var request = _calendarService.Events.Insert(newEvent, _calendarId);
        request.SendUpdates = EventsResource.InsertRequest.SendUpdatesEnum.All;
        var created = await request.ExecuteAsync();
        return created.Id;
    }

    public async Task<BookedEvent> RescheduleEventAsync(string eventId, DateTime newStart, DateTime newEnd)
    {
        var existing = await _calendarService.Events.Get(_calendarId, eventId).ExecuteAsync();

        existing.Start = new EventDateTime { DateTimeDateTimeOffset = newStart, TimeZone = "Pacific/Auckland" };
        existing.End = new EventDateTime { DateTimeDateTimeOffset = newEnd, TimeZone = "Pacific/Auckland" };

        var updateRequest = _calendarService.Events.Update(existing, _calendarId, eventId);
        updateRequest.SendUpdates = EventsResource.UpdateRequest.SendUpdatesEnum.All;
        var updated = await updateRequest.ExecuteAsync();

        var customerAttendee = updated.Attendees?.FirstOrDefault();

        return new BookedEvent(
            updated.Id,
            newStart,
            newEnd,
            customerAttendee?.DisplayName ?? "Customer",
            customerAttendee?.Email ?? string.Empty
        );
    }

    public async Task CancelEventAsync(string eventId)
    {
        var deleteRequest = _calendarService.Events.Delete(_calendarId, eventId);
        deleteRequest.SendUpdates = EventsResource.DeleteRequest.SendUpdatesEnum.All;
        await deleteRequest.ExecuteAsync();
    }

    private async Task<List<TimeSlot>> GetBusyTimesAsync(DateTime rangeStart, DateTime rangeEnd)
    {
        var freeBusyRequest = new FreeBusyRequest
        {
            TimeMin = rangeStart,
            TimeMax = rangeEnd,
            Items = new List<FreeBusyRequestItem> { new() { Id = _calendarId } }
        };

        var freeBusyResponse = await _calendarService.Freebusy.Query(freeBusyRequest).ExecuteAsync();
        var busy = freeBusyResponse.Calendars[_calendarId].Busy;

        return busy.Select(b => new TimeSlot(
            b.Start!.Value,
            b.End!.Value
        )).ToList();
    }
}