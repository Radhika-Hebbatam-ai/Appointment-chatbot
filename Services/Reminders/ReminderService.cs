using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: Background service that sends the business owner a daily evening
///       reminder of tomorrow's appointments with service details and prerequisites.
/// HOW:  Runs as IHostedService — starts with the app and checks the time
///       every minute. When current time matches ReminderTime in BusinessConfig
///       (e.g. 18:00), fires the reminder job once and sets a flag to prevent
///       sending twice. Flag resets at midnight for the next day.
/// </summary>
public class AppointmentReminderService : BackgroundService, IReminderService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BusinessConfig _config;
    private readonly ILogger<AppointmentReminderService> _logger;

    // Tracks whether today's reminder has already been sent
    private DateTime _lastReminderDate = DateTime.MinValue;

    public AppointmentReminderService(
        IServiceScopeFactory scopeFactory,
        IOptions<BusinessConfig> config,
        ILogger<AppointmentReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// WHAT: Background loop — checks time every minute and fires reminder at configured time.
    /// HOW:  Uses CancellationToken from IHostedService to stop cleanly when app shuts down.
    ///       Delays 60 seconds between checks to avoid burning CPU.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Reminder service started. Will send daily reminder at {Time}",
            _config.ReminderTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSendReminderAsync();
            }
            catch (Exception ex)
            {
                // Log but don't crash the background service — retry next minute
                _logger.LogError(ex, "Error checking/sending reminder");
            }

            // Wait 60 seconds before checking again
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>
    /// WHAT: Checks if it's time to send today's reminder.
    /// HOW:  Parses ReminderTime from config, compares to current local time.
    ///       Uses _lastReminderDate flag to ensure reminder only sends once per day.
    /// </summary>
    private async Task CheckAndSendReminderAsync()
    {
        if (!TimeSpan.TryParse(_config.ReminderTime, out var reminderTime))
        {
            _logger.LogWarning("Invalid ReminderTime format: {Time}. Use HH:mm e.g. 18:00",
                _config.ReminderTime);
            return;
        }

        var now = DateTime.Now;
        var scheduledToday = now.Date + reminderTime;

        // Only send if:
        // 1. Current time has passed the scheduled time
        // 2. Haven't already sent today's reminder
        if (now >= scheduledToday && _lastReminderDate.Date != now.Date)
        {
            _logger.LogInformation("Sending evening reminder for {Date}", now.Date);
            await SendEveningReminderAsync();
            _lastReminderDate = now;
        }
    }

    /// <summary>
    /// WHAT: Builds and sends the evening reminder email to the business owner.
    /// HOW:  Creates a new DI scope (required for scoped services in a singleton
    ///       background service), fetches tomorrow's calendar events, looks up
    ///       prerequisites for each service via RAG, builds formatted email body,
    ///       sends via IEmailService to the owner's email address.
    /// </summary>
    public async Task SendEveningReminderAsync()
    {
        // Background services are singletons but ICalendarService etc may be scoped
        // — always resolve them via a new scope, never inject directly
        using var scope = _scopeFactory.CreateScope();
        var calendarService = scope.ServiceProvider
            .GetRequiredService<ICalendarService>();
        var emailService = scope.ServiceProvider
            .GetRequiredService<IEmailService>();
        var documentIndex = scope.ServiceProvider
            .GetRequiredService<IDocumentIndexService>();

        var tomorrow = DateTime.Now.Date.AddDays(1);

        // Get all events for tomorrow
        var slots = await calendarService.GetAvailableSlotsAsync(tomorrow);

        // Get busy/booked slots by comparing total slots vs available
        // For a proper event list we use GetEventsForDateAsync (added below)
        var events = await calendarService.GetEventsForDateAsync(tomorrow);

        if (!events.Any())
        {
            _logger.LogInformation("No appointments tomorrow — skipping reminder");
            return;
        }

        // Build email body with full details per appointment
        var lines = new List<string>
        {
            $"Hi,",
            $"",
            $"Here are tomorrow's appointments ({tomorrow:dddd, d MMMM yyyy}):",
            $""
        };

        foreach (var evt in events.OrderBy(e => e.Start))
        {
            lines.Add($"{evt.Start:h:mm tt} — {evt.CustomerName}");

            // RAG lookup: find prerequisites for this service
            var prerequisites = await documentIndex
                .SearchAsync($"prerequisites for {evt.Description}", topN: 1);

            if (prerequisites.Any())
                lines.Add($"   Note: {prerequisites.First()}");

            lines.Add("");
        }

        lines.Add($"Total appointments: {events.Count}");
        lines.Add("");
        lines.Add($"Have a great evening,");
        lines.Add(_config.Name);

        var body = string.Join("\n", lines);
        var subject = $"Tomorrow's Appointments — {tomorrow:d MMMM yyyy}";

        await emailService.SendEveningReminderAsync(
            _config.OwnerEmail, subject, body);

        _logger.LogInformation(
            "Evening reminder sent for {Count} appointments", events.Count);
    }
}