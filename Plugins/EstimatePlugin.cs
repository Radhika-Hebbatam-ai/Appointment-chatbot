using System.ComponentModel;
using Microsoft.SemanticKernel;
using AppointmentChatbot.Services;

namespace AppointmentChatbot.Plugins;

/// <summary>
/// WHAT: Estimate Agent — looks up pricing and prerequisites for a service.
/// HOW:  Uses RAG (IDocumentIndexService) to search the business's own
///       documents — pricing sheets, service descriptions, prerequisites.
///       Called by the Orchestrator as soon as a customer names a service.
///       Single responsibility: answer "what does this cost and what do I need?"
/// </summary>
public class EstimatePlugin
{
    private readonly IDocumentIndexService _documentIndex;

    public EstimatePlugin(IDocumentIndexService documentIndex)
    {
        _documentIndex = documentIndex;
    }

    [KernelFunction("get_service_estimate")]
    [Description("Looks up the price and prerequisites for a specific service. " +
                 "Call this as soon as the customer names an appointment type.")]
    public async Task<string> GetServiceEstimateAsync(
        [Description("The service the customer mentioned e.g. 'blood test', 'dental cleaning'")]
        string serviceDescription)
    {
        var results = await _documentIndex.SearchAsync(serviceDescription, topN: 2);

        if (results.Count == 0)
            return "NO_MATCH: no pricing information found for that service. " +
                   "Tell the customer to confirm cost with the provider directly.";

        return "MATCHED_SERVICE_INFO:\n" + string.Join("\n---\n", results);
    }
}