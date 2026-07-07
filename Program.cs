#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0001

using Microsoft.Extensions.Options;
using AppointmentChatbot.Services;
using AppointmentChatbot.Plugins;
using AppointmentChatbot.Services.Staff;


var builder = WebApplication.CreateBuilder(args);

// ── Controllers ────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── CORS ───────────────────────────────────────────────────────────────────
// Wide open for Step 1 — tighten to specific origins when JWT is added
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWidget", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// ── HttpClient ─────────────────────────────────────────────────────────────
// Required by DocumentIndexService for Azure OpenAI embeddings calls
builder.Services.AddHttpClient();

// ── BusinessConfig ─────────────────────────────────────────────────────────
// Binds the "BusinessConfig" section in appsettings.json to the
// strongly-typed BusinessConfig class — injected via IOptions<BusinessConfig>
builder.Services.Configure<BusinessConfig>(
    builder.Configuration.GetSection(BusinessConfig.SectionName));

// ── Calendar Service ───────────────────────────────────────────────────────
// Reads CalendarProvider from config and injects the right implementation.
// Switching a business from Google to Outlook = change one line in appsettings.json
var calendarProvider = builder.Configuration["CalendarProvider"] ?? "Google";
if (calendarProvider == "Outlook")
    builder.Services.AddSingleton<ICalendarService, OutlookCalendarService>();
else
    builder.Services.AddSingleton<ICalendarService, GoogleCalendarService>();

// ── Email Service ──────────────────────────────────────────────────────────
// Azure Communication Services email — sends from business owner's address
builder.Services.AddSingleton<IEmailService, AzureCommunicationEmailService>();

// ── Booking Store ──────────────────────────────────────────────────────────
// In-memory for Step 1 — swap for SqlBookingStore in Step 4
builder.Services.AddSingleton<IBookingStore, InMemoryBookingStore>();

// ── Document Index Service ─────────────────────────────────────────────────
// RAG layer — embeds business documents at startup for pricing/prerequisites search
builder.Services.AddSingleton<IDocumentIndexService, DocumentIndexService>();

// ── Staff Store ────────────────────────────────────────────────────────────
// Reads staff contacts from CSV or Excel file configured in BusinessConfig
builder.Services.AddSingleton<IStaffStore, StaffStore>();

// ── Plugins (Semantic Kernel Agents) ───────────────────────────────────────
// Each plugin is a specialist agent with one responsibility
builder.Services.AddSingleton<EstimatePlugin>();
builder.Services.AddSingleton<AvailabilityPlugin>();
builder.Services.AddSingleton<BookingPlugin>();
builder.Services.AddSingleton<ReschedulingPlugin>();
builder.Services.AddSingleton<NotificationPlugin>();

// ── Chat Orchestrator ──────────────────────────────────────────────────────
// Semantic Kernel multi-agent orchestrator — wires all plugins to the LLM
builder.Services.AddSingleton<IChatOrchestrator, SemanticKernelChatOrchestrator>();

// ── Evening Reminder Background Service ───────────────────────────────────
// Runs continuously, sends owner reminder at configured ReminderTime each day
builder.Services.AddHostedService<AppointmentReminderService>();

// ── Build App ──────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Index Documents at Startup ─────────────────────────────────────────────
// Embeds all documents in DocumentsPath so RAG is ready before first request
using (var scope = app.Services.CreateScope())
{
    var documentIndex = scope.ServiceProvider
        .GetRequiredService<IDocumentIndexService>();
    await documentIndex.IndexDocumentsAsync();
}

// ── Middleware Pipeline ────────────────────────────────────────────────────
app.UseCors("AllowWidget");
app.UseDefaultFiles();
app.UseStaticFiles();   // serves wwwroot/ (widget.js, index.html, upload.html)
app.MapControllers();

app.Run();