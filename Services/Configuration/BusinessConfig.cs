namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: Strongly-typed configuration for a single business tenant.
/// HOW:  Bound to the "BusinessConfig" section in appsettings.json
///       via the options pattern. Change any value here without touching
///       code — just update appsettings.json and restart the app.
/// </summary>
public class BusinessConfig
{
    public const string SectionName = "BusinessConfig";

    /// <summary>
    /// Display name of the business shown in emails and chat widget.
    /// e.g. "Riverside Family Clinic"
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Business owner's personal email — receives evening reminders.
    /// e.g. "sarah@riversidefamilyclinic.co.nz"
    /// </summary>
    public string OwnerEmail { get; set; } = string.Empty;

    /// <summary>
    /// The FROM address customers see on all booking emails.
    /// Must be verified in Azure Communication Services.
    /// e.g. "bookings@riversidefamilyclinic.co.nz"
    /// </summary>
    public string BookingEmail { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown alongside BookingEmail in customer's inbox.
    /// e.g. "Riverside Family Clinic Bookings"
    /// </summary>
    public string BookingEmailName { get; set; } = string.Empty;

    /// <summary>
    /// Azure Communication Services connection string.
    /// Found in Azure Portal → Communication Services → Keys.
    /// Emails sent from BookingEmail using this connection.
    /// </summary>
    public string AcsConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Fully qualified path to the documents folder (pricing, prerequisites).
    /// Business owner drops .txt/.pdf/.docx files here — no redeployment needed.
    /// e.g. "C:\\BusinessDocs\\Clinic\\" or "/home/docs/clinic/"
    /// </summary>
    public string DocumentsPath { get; set; } = string.Empty;

    /// <summary>
    /// Fully qualified path to the staff contacts file (.csv or .xlsx).
    /// Business owner maintains this file directly — add/remove staff anytime.
    /// e.g. "C:\\BusinessDocs\\Clinic\\staff.csv"
    /// </summary>
    public string StaffFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Time to send the evening appointment reminder to the owner.
    /// 24-hour format HH:mm e.g. "18:00"
    /// </summary>
    public string ReminderTime { get; set; } = "18:00";

    /// <summary>
    /// IANA timezone name used for all calendar operations and email times.
    /// e.g. "Pacific/Auckland"
    /// </summary>
    public string TimeZone { get; set; } = "Pacific/Auckland";
}