using System.Text.Json;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AppointmentChatbot.Services;

/// <summary>
/// WHAT: RAG document index — loads business documents, embeds them, and
///       supports semantic search over the content.
/// HOW:  Extracts text from .txt/.pdf/.docx files, splits into chunks,
///       embeds each chunk via Azure OpenAI text-embedding-3-small,
///       stores vectors in memory, uses cosine similarity for search.
///       Swap this for AzureSearchDocumentIndexService in a later step
///       without changing any plugin or controller code.
/// </summary>
public class DocumentIndexService : IDocumentIndexService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _embeddingDeployment;
    private readonly string _documentsPath;

    // In-memory vector store — list of (text chunk, source file, embedding vector)
    private List<DocumentChunk> _chunks = new();

    // Prevents two threads from re-indexing simultaneously
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public DocumentIndexService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment env)
    {
        _httpClient = httpClientFactory.CreateClient();

        _endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured");
        _apiKey = config["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("AzureOpenAI:ApiKey not configured");
        _embeddingDeployment = config["AzureOpenAI:EmbeddingDeploymentName"]
            ?? throw new InvalidOperationException(
                "AzureOpenAI:EmbeddingDeploymentName not configured");

        _documentsPath = Path.Combine(env.ContentRootPath, "Documents");
    }

    /// <summary>
    /// WHAT: Indexes all supported documents in the /Documents folder.
    /// HOW:  Iterates .txt/.pdf/.docx files, extracts text via format-specific
    ///       extractors, splits into chunks, embeds each chunk via Azure OpenAI,
    ///       replaces the in-memory chunk list atomically.
    /// </summary>
    public async Task IndexDocumentsAsync()
    {
        await _indexLock.WaitAsync();
        try
        {
            var newChunks = new List<DocumentChunk>();

            if (!Directory.Exists(_documentsPath))
                return;

            // Collect all supported file types
            var supportedExtensions = new[] { "*.txt", "*.pdf", "*.docx" };
            var files = supportedExtensions
                .SelectMany(ext => Directory.GetFiles(_documentsPath, ext))
                .ToList();

            foreach (var file in files)
            {
                // Extract raw text using the right extractor for this file type
                var text = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".txt"  => await ExtractFromTxtAsync(file),
                    ".pdf"  => ExtractFromPdf(file),
                    ".docx" => ExtractFromDocx(file),
                    _       => null
                };

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Split into chunks and embed each one
                var chunks = ChunkText(text);
                foreach (var chunkText in chunks)
                {
                    var embedding = await GetEmbeddingAsync(chunkText);
                    newChunks.Add(new DocumentChunk(
                        chunkText,
                        Path.GetFileName(file),
                        embedding));
                }
            }

            // Atomically replace the old index
            _chunks = newChunks;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// WHAT: Finds the most relevant document chunks for a customer's query.
    /// HOW:  Embeds the query, computes cosine similarity against all stored
    ///       chunk embeddings, returns the top N highest-scoring chunk texts.
    /// </summary>
    public async Task<List<string>> SearchAsync(string query, int topN = 3)
    {
        if (_chunks.Count == 0)
            return new List<string>();

        var queryEmbedding = await GetEmbeddingAsync(query);

        return _chunks
            .Select(c => (Chunk: c, Score: CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .Select(x => x.Chunk.Text)
            .ToList();
    }

    /// <summary>
    /// WHAT: Reads a plain text file.
    /// HOW:  Standard async file read — simplest case.
    /// </summary>
    private static async Task<string> ExtractFromTxtAsync(string filePath)
        => await File.ReadAllTextAsync(filePath);

    /// <summary>
    /// WHAT: Extracts all text from a PDF file.
    /// HOW:  Uses PdfPig to open the PDF and iterate pages,
    ///       concatenating all words on each page into plain text.
    ///       Works on text-based PDFs — scanned image PDFs need OCR (later step).
    /// </summary>
    private static string ExtractFromPdf(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);
        var pages = pdf.GetPages()
            .Select(page => string.Join(" ", page.GetWords().Select(w => w.Text)));
        return string.Join("\n", pages);
    }

    /// <summary>
    /// WHAT: Extracts all text from a Word (.docx) file.
    /// HOW:  Uses DocumentFormat.OpenXml to open the package,
    ///       reads all Paragraph elements from the document body,
    ///       joins them with newlines into plain text.
    /// </summary>
    private static string ExtractFromDocx(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return string.Empty;

        var paragraphs = body
            .Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return string.Join("\n", paragraphs);
    }

    /// <summary>
    /// WHAT: Splits raw document text into searchable chunks.
    /// HOW:  Splits on blank lines — works naturally for structured pricing
    ///       documents where each service entry is separated by a blank line.
    ///       Each chunk becomes one independently searchable unit.
    /// </summary>
    private static List<string> ChunkText(string text)
    {
        return text
            .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .ToList();
    }

    /// <summary>
    /// WHAT: Converts text into a vector embedding via Azure OpenAI.
    /// HOW:  Calls the Azure OpenAI embeddings REST endpoint directly via HttpClient
    ///       (same pattern as Product Catalogue NL search layer).
    ///       Returns a float[] representing the semantic meaning of the text.
    /// </summary>
    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        var url = $"{_endpoint.TrimEnd('/')}/openai/deployments/" +
                  $"{_embeddingDeployment}/embeddings?api-version=2023-05-15";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _apiKey);
        request.Content = JsonContent.Create(new { input = text });

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();
    }

    /// <summary>
    /// WHAT: Measures semantic similarity between two embedding vectors.
    /// HOW:  Cosine similarity = dot product / (magnitude A × magnitude B).
    ///       Returns 1.0 for identical meaning, 0.0 for unrelated, -1.0 for opposite.
    ///       The + 1e-8f prevents division by zero if either vector is all zeros.
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB) + 1e-8f);
    }
}