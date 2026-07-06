namespace AppointmentChatbot.Services;

public record DocumentChunk(string Text, string SourceFile, float[] Embedding);

public interface IDocumentIndexService
{
    /// <summary>
    /// WHAT: Loads and embeds all supported documents in the /Documents folder.
    /// HOW: Reads .txt, .pdf, and .docx files, extracts text, splits into chunks,
    ///      embeds each chunk via Azure OpenAI, stores in memory for search.
    /// Supported formats: .txt, .pdf, .docx
    /// Call once at startup. Safe to call again to re-index if documents change.
    /// </summary>
    Task IndexDocumentsAsync();

    /// <summary>
    /// WHAT: Returns the top N most relevant document chunks for a given query.
    /// HOW: Embeds the query via Azure OpenAI, computes cosine similarity against
    ///      all stored chunk embeddings, returns the highest scoring chunks.
    /// </summary>
    Task<List<string>> SearchAsync(string query, int topN = 3);
}