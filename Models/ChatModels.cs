namespace AppointmentChatbot.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;

    // Basic in-memory history — no session/JWT yet (that's a later step)
    public List<ChatTurn> History { get; set; } = new();
}

public class ChatTurn
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public List<ChatTurn> History { get; set; } = new();
}