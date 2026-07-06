using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AppointmentChatbot.Services;
using AppointmentChatbot.Services.Staff;

namespace AppointmentChatbot.Controllers;

/// <summary>
/// WHAT: Admin endpoints for business owner file management.
/// HOW:  Protected by a simple API key header (X-Admin-Key) until
///       JWT authentication is added in Step 2. Business owner uploads
///       staff list and pricing documents via these endpoints — no
///       server access or developer involvement required.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IStaffStore _staffStore;
    private readonly IDocumentIndexService _documentIndex;
    private readonly BusinessConfig _config;
    private readonly string _adminApiKey;

    private static readonly string[] AllowedStaffExtensions
        = new[] { ".csv", ".xlsx" };

    private static readonly string[] AllowedDocumentExtensions
        = new[] { ".txt", ".pdf", ".docx" };

    public AdminController(
        IStaffStore staffStore,
        IDocumentIndexService documentIndex,
        IOptions<BusinessConfig> config,
        IConfiguration appConfig)
    {
        _staffStore = staffStore;
        _documentIndex = documentIndex;
        _config = config.Value;
        _adminApiKey = appConfig["AdminApiKey"]
            ?? throw new InvalidOperationException("AdminApiKey not configured");
    }

    /// <summary>
    /// WHAT: Uploads a new staff contacts file (CSV or Excel).
    /// HOW:  Validates API key, validates file type, saves to configured
    ///       StaffFilePath, then reloads the staff store immediately
    ///       so changes take effect without restarting the app.
    /// </summary>
    [HttpPost("upload/staff")]
    public async Task<IActionResult> UploadStaffAsync(IFormFile file)
    {
        if (!IsAuthorized())
            return Unauthorized("Invalid or missing X-Admin-Key header");

        if (file == null || file.Length == 0)
            return BadRequest("No file provided");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedStaffExtensions.Contains(ext))
            return BadRequest(
                $"Only {string.Join(", ", AllowedStaffExtensions)} files are supported");

        if (string.IsNullOrWhiteSpace(_config.StaffFilePath))
            return BadRequest("StaffFilePath not configured in appsettings.json");

        // Ensure directory exists before writing
        var directory = Path.GetDirectoryName(_config.StaffFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // Save uploaded file to configured path
        await using (var stream = new FileStream(_config.StaffFilePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Reload immediately — no restart needed
        await _staffStore.ReloadAsync();

        return Ok(new
        {
            message = "Staff list updated successfully",
            staffCount = _staffStore.GetAll().Count,
            staff = _staffStore.GetAll().Select(s => new { s.Name, s.Email })
        });
    }

    /// <summary>
    /// WHAT: Uploads a pricing/prerequisites document (TXT, PDF, or DOCX).
    /// HOW:  Validates API key, validates file type, saves to configured
    ///       DocumentsPath, then re-indexes all documents immediately
    ///       so RAG picks up the new content without restart.
    /// </summary>
    [HttpPost("upload/document")]
    public async Task<IActionResult> UploadDocumentAsync(IFormFile file)
    {
        if (!IsAuthorized())
            return Unauthorized("Invalid or missing X-Admin-Key header");

        if (file == null || file.Length == 0)
            return BadRequest("No file provided");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedDocumentExtensions.Contains(ext))
            return BadRequest(
                $"Only {string.Join(", ", AllowedDocumentExtensions)} files are supported");

        if (string.IsNullOrWhiteSpace(_config.DocumentsPath))
            return BadRequest("DocumentsPath not configured in appsettings.json");

        // Ensure documents directory exists
        Directory.CreateDirectory(_config.DocumentsPath);

        // Save using original filename inside documents folder
        var filePath = Path.Combine(
            _config.DocumentsPath,
            Path.GetFileName(file.FileName));

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Re-index all documents — RAG picks up new content immediately
        await _documentIndex.IndexDocumentsAsync();

        return Ok(new
        {
            message = $"{file.FileName} uploaded and indexed successfully",
            fileName = file.FileName,
            sizeBytes = file.Length
        });
    }

    /// <summary>
    /// WHAT: Returns current staff list — useful for verifying an upload worked.
    /// HOW:  Simple read from in-memory staff store, no file I/O.
    /// </summary>
    [HttpGet("staff")]
    public IActionResult GetStaff()
    {
        if (!IsAuthorized())
            return Unauthorized("Invalid or missing X-Admin-Key header");

        return Ok(new
        {
            staffCount = _staffStore.GetAll().Count,
            staff = _staffStore.GetAll().Select(s => new { s.Name, s.Email })
        });
    }

    /// <summary>
    /// WHAT: Checks X-Admin-Key header against configured admin key.
    /// HOW:  Simple string comparison — replaced by JWT in Step 2.
    /// </summary>
    private bool IsAuthorized() =>
        Request.Headers.TryGetValue("X-Admin-Key", out var key)
        && key == _adminApiKey;
}