#pragma warning disable SKEXP0110
#pragma warning disable SKEXP0001

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Options;
using AppointmentChatbot.Models;
using AppointmentChatbot.Plugins;
using AppointmentChatbot.Services;

namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: The Orchestrator — the only agent that talks directly to the user.
/// HOW:  Uses Semantic Kernel ChatCompletionAgent with all 5 specialist
///       plugins registered as callable tools. Holds conversation history
///       per request and lets the LLM decide which plugins to call and when.
///       This is the "agents-as-tools" multi-agent pattern — each specialist
///       plugin has a single responsibility, the orchestrator coordinates them.
/// </summary>
public interface IChatOrchestrator
{
    /// <summary>
    /// WHAT: Handles a single user message and returns the assistant's reply.
    /// HOW:  Passes message + history to the Semantic Kernel agent, which
    ///       calls plugins as needed and returns the final text response.
    ///       Returns updated history so the widget can maintain conversation context.
    /// </summary>
    Task<(string reply, List<ChatTurn> updatedHistory)> HandleMessageAsync(
        string userMessage, List<ChatTurn> history);
}

public class SemanticKernelChatOrchestrator : IChatOrchestrator
{
    private readonly ChatCompletionAgent _agent;

    // System prompt — tells the LLM its role, rules, and when to use each plugin
    private const string SystemPrompt = """
        You are a friendly appointment booking assistant for a business.

        Your job is to help customers:
        1. Understand the cost and prerequisites for a service (use get_service_estimate)
        2. Find an available appointment slot (use check_availability)
        3. Book a confirmed slot (use book_appointment, then send_confirmation_email)
        4. Reschedule an existing appointment (use reschedule_appointment, then send_reschedule_email)
        5. Cancel an appointment (use send_cancellation_email after cancelling)

        Rules:
        - As soon as the customer names a service, call get_service_estimate and tell
          them the price and prerequisites BEFORE checking availability.
        - Never call book_appointment without first confirming the slot is free
          AND getting explicit customer confirmation to proceed.
        - Always collect customer's full name and email before booking.
        - Always ask for the provider's name or email if not provided.
        - If a requested slot is not available, offer the alternatives from check_availability.
        - After every successful booking or reschedule, always send the notification email.
        - Remind the customer of any prerequisites in the final booking confirmation.
        - Keep responses short, warm, and easy to read in a chat widget.
        """;

    public SemanticKernelChatOrchestrator(
        IConfiguration config,
        EstimatePlugin estimate,
        AvailabilityPlugin availability,
        BookingPlugin booking,
        ReschedulingPlugin rescheduling,
        NotificationPlugin notification)
    {
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured");
        var apiKey = config["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey not configured");
        var deployment = config["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName not configured");

        // Build the Kernel — the Semantic Kernel runtime
        var builder = Kernel.CreateBuilder();

        // Connect to Azure OpenAI — this is the LLM that drives the orchestrator
        builder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey);

        // Register all 5 specialist plugins as callable tools
        // The LLM reads their [Description] attributes to decide when to call each one
        builder.Plugins.AddFromObject(estimate, "Estimate");
        builder.Plugins.AddFromObject(availability, "Availability");
        builder.Plugins.AddFromObject(booking, "Booking");
        builder.Plugins.AddFromObject(rescheduling, "Rescheduling");
        builder.Plugins.AddFromObject(notification, "Notification");

        var kernel = builder.Build();

        // ChatCompletionAgent — the orchestrator agent
        // FunctionChoiceBehavior.Auto() lets the LLM decide when to call tools
        // and when to respond with text — no manual loop needed
        _agent = new ChatCompletionAgent
        {
            Name = "AppointmentOrchestrator",
            Instructions = SystemPrompt,
            Kernel = kernel,
            Arguments = new KernelArguments(new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
        };
    }

    /// <summary>
    /// WHAT: Handles a single user message turn.
    /// HOW:  Rebuilds ChatHistory from previous turns (stateless per request —
    ///       history is maintained by the widget and passed back each time),
    ///       adds the new user message, invokes the agent, collects the reply,
    ///       returns updated history for the widget to store.
    /// </summary>
    public async Task<(string reply, List<ChatTurn> updatedHistory)> HandleMessageAsync(
        string userMessage, List<ChatTurn> history)
    {
        // Rebuild full conversation history from previous turns
        var chatHistory = new ChatHistory();
        foreach (var turn in history)
        {
            if (turn.Role == "user")
                chatHistory.AddUserMessage(turn.Content);
            else
                chatHistory.AddAssistantMessage(turn.Content);
        }

        // Add the new user message
        chatHistory.AddUserMessage(userMessage);

        // Invoke the agent — SK handles all tool calls internally
        // The agent may call multiple plugins before returning a final reply
        string reply = string.Empty;
        await foreach (var response in _agent.InvokeAsync(chatHistory))
        {
            reply = response.Content ?? string.Empty;
        }
        // Append this turn to history so widget can pass it back next time
        var updatedHistory = new List<ChatTurn>(history)
        {
            new() { Role = "user",      Content = userMessage },
            new() { Role = "assistant", Content = reply }
        };

        return (reply, updatedHistory);
    }
}