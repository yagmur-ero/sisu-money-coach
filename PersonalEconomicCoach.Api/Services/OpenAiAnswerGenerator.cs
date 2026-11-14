using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PersonalEconomicCoach.Api.Services;

public interface IAnswerGenerator
{
    Task<string> GenerateAsync(
        string question,
        string tone,
        string insightType,
        object data,
        CancellationToken cancellationToken);
}

/// <summary>
/// The model receives already-calculated insight data. It is only responsible for phrasing the answer;
/// it does not classify transactions, calculate totals, or execute SQL.
/// </summary>
public sealed class OpenAiAnswerGenerator(HttpClient httpClient, IConfiguration configuration) : IAnswerGenerator
{
    public async Task<string> GenerateAsync(
        string question,
        string tone,
        string insightType,
        object data,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Ai:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApiException(503, "AI is not configured. Set the Ai:ApiKey environment variable, or use the direct /insights endpoints.");

        var baseUrl = configuration["Ai:BaseUrl"] ?? "https://api.openai.com/v1/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        var endpoint = new Uri(new Uri(baseUrl), "chat/completions");
        var model = configuration["Ai:Model"] ?? "gpt-4.1-mini";
        var maxTokens = configuration.GetValue<int>("Ai:MaxOutputTokens", 350);

        var systemPrompt = $"""
You are Personal Economic Coach, a practical and respectful personal finance coach.
Write only the user-facing answer. The backend has already calculated all numbers and classified all transactions.
Use only facts and amounts present in the supplied JSON data. Never invent, recalculate, or infer unsupported financial figures.
If the data does not support a conclusion, say so clearly. Do not give investment, credit, tax, or regulated financial advice.
Tone: {NormalizeTone(tone)}.
For 'roast', use light playful humor only; never shame the user or judge their financial situation.
Be concise, explain the most useful insight, and suggest one practical next step when supported by the data.
The insight type is {insightType}.
""";

        var dataJson = JsonSerializer.Serialize(data, JsonOptions);
        var payload = new
        {
            model,
            temperature = 0.35,
            max_completion_tokens = maxTokens,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Question: {question}\n\nVerified backend data (JSON):\n{dataJson}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                throw new ApiException(503,
                    $"AI answer generation is temporarily unavailable (provider status {statusCode}). Use the direct /insights endpoints as a fallback.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || choices[0].ValueKind != JsonValueKind.Object
                || !choices[0].TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                throw new ApiException(503,
                    "The AI provider returned an unexpected response. Use the direct /insights endpoints as a fallback.");
            }

            var answer = content.GetString();
            if (string.IsNullOrWhiteSpace(answer))
                throw new ApiException(503,
                    "The AI provider returned an empty answer. Use the direct /insights endpoints as a fallback.");

            return answer.Trim();
        }
        catch (HttpRequestException)
        {
            throw new ApiException(503,
                "The AI provider could not be reached. Use the direct /insights endpoints as a fallback.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException(503,
                "The AI provider request timed out. Use the direct /insights endpoints as a fallback.");
        }
        catch (JsonException)
        {
            throw new ApiException(503,
                "The AI provider returned invalid JSON. Use the direct /insights endpoints as a fallback.");
        }
    }

    private static string NormalizeTone(string tone) => tone.ToLowerInvariant() switch
    {
        "gentle" => "Gentle, supportive, encouraging, and non-judgmental.",
        "honest" => "Direct, candid, and practical, but respectful.",
        "roast" => "Lightly humorous and playful, but never cruel or shaming.",
        _ => "Gentle and respectful."
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
