namespace AppointmentChatbot.Services.Staff;

public record StaffMember(string Name, string Email);

/// <summary>
/// WHAT: Contract for looking up staff/provider contact details.
/// HOW:  Implementations read from CSV or Excel files configured
///       via BusinessConfig.StaffFilePath — no code changes needed
///       when the business owner updates their staff list.
/// </summary>
public interface IStaffStore
{
    /// <summary>
    /// WHAT: Returns all staff members loaded from the configured file.
    /// HOW:  File is read once at startup and cached in memory.
    /// </summary>
    IReadOnlyList<StaffMember> GetAll();

    /// <summary>
    /// WHAT: Finds a staff member's email by their name.
    /// HOW:  Case-insensitive name match against the in-memory list.
    ///       Returns null if no match found.
    /// </summary>
    string? GetEmailByName(string name);

    /// <summary>
    /// WHAT: Re-reads the staff file from disk and refreshes the in-memory list.
    /// HOW:  Called after an admin uploads a new staff file, so changes
    ///       take effect without restarting the app.
    /// </summary>
    Task ReloadAsync();
}