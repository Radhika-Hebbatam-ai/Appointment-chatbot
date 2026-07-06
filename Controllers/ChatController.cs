using Microsoft.AspNetCore.Mvc;
using AppointmentChatbot.Models;
using AppointmentChatbot.Services;

namespace AppointmentChatbot.Controllers;

/// <summary>
/// WHAT: The single API endpoint the chat widget talks to.
/// HOW:  Receives a user message + conversation history from the widget,
///       passes it to IChatOrchestrator which runs the Semantic Kernel
///       multi-agent loop, returns the assistant reply + updated history.
///       Stateless — conversation history is maintained by the widget
///       and sent back with every request (no server-side session needed).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatOrchestrator _orchestrator;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IChatOrchestrator orchestrator,
        ILogger<ChatController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// WHAT: Handles a single chat message from the widget.
    /// HOW:  Validates the request, delegates to the orchestrator,
    ///       returns reply + updated history to the widget.
    ///       POST /api/chat
    ///       Body: { "message": "...", "history": [...] }
    ///       Response: { "reply": "...", "history": [...] }
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> PostAsync(
        [FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty");

        _logger.LogInformation(
            "Chat message received: {Length} chars, history: {Count} turns",
            request.Message.Length,
            request.History.Count);

        var (reply, updatedHistory) = await _orchestrator
            .HandleMessageAsync(request.Message, request.History);

        _logger.LogInformation("Chat reply sent: {Length} chars", reply.Length);

        return Ok(new ChatResponse
        {
            Reply = reply,
            History = updatedHistory
        });
    }
}