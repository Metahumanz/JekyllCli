using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlogTools.Models;

namespace BlogTools.Services
{
    /// <summary>
    /// Handles AI-powered commit message generation via OpenAI-compatible APIs.
    /// </summary>
    public class AiCommitMessageService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const int MaxDiffChars = 12_000;

        // ── JSON models for /models endpoint ────────────────────

    private class ModelListResponse
    {
        [JsonPropertyName("data")]
        public List<ModelEntry> Data { get; set; } = new();
    }

    private class ModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    // ── JSON models for /chat/completions ──────────────────────

    private class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 200;
    }

    private class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; set; } = new();
    }

    private class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    // ── Public API ─────────────────────────────────────────────

        /// <summary>
        /// Fetch available model IDs from the provider's /models endpoint.
        /// Returns null on failure; callers should fall back to presets.
        /// </summary>
        public static async Task<List<string>?> FetchModelsAsync(AiCommitProfile profile, string decryptedKey)
        {
            var url = AiProviderPresets.GetEffectiveModelsUrl(profile);
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(decryptedKey))
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {decryptedKey}");

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync();
                var modelList = JsonSerializer.Deserialize<ModelListResponse>(body);
                return modelList?.Data
                    ?.Select(m => m.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Test AI generation by asking for a short response.
        /// Returns (success, generatedText, errorMessage).
        /// </summary>
        public static async Task<(bool Success, string GeneratedText, string ErrorMessage)> TestGenerateAsync(
            AiCommitProfile profile, string decryptedKey)
        {
            try
            {
                var request = new ChatRequest
                {
                    Model = profile.Model,
                    Messages = new List<ChatMessage>
                    {
                        new() { Role = "user", Content = "Say exactly: Hello from JekyllCli!" }
                    },
                    Temperature = 0.3,
                    MaxTokens = 50
                };

                var (success, text, error) = await SendChatRequestAsync(profile.BaseUrl, decryptedKey, request);
                return (success, text, error);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Generate a commit message from a diff summary.
        /// </summary>
        /// <param name="profile">The AI profile to use.</param>
        /// <param name="decryptedKey">Decrypted API key.</param>
        /// <param name="diffSummary">Pre-formatted diff summary (truncated).</param>
        /// <param name="style">Commit message style.</param>
        /// <param name="language">Output language preference.</param>
        /// <param name="uiLanguage">The current UI language code for "Follow UI" mode.</param>
        /// <returns>(success, commitMessage, errorMessage)</returns>
        public static async Task<(bool Success, string CommitMessage, string ErrorMessage)> GenerateCommitMessageAsync(
            AiCommitProfile profile,
            string decryptedKey,
            string diffSummary,
            AiCommitStyle style,
            AiCommitLanguage language,
            string uiLanguage)
        {
            try
            {
                var styleInstruction = style switch
                {
                    AiCommitStyle.ConventionalCommit => "Use Conventional Commits format (e.g. 'feat:', 'fix:', 'docs:', 'chore:', 'style:', 'refactor:').",
                    AiCommitStyle.SingleLine => "Write a concise single-line summary.",
                    _ => "Write a concise single-line summary."
                };

                var langInstruction = ResolveLanguageInstruction(language, uiLanguage);

                var systemPrompt = new StringBuilder();
                systemPrompt.AppendLine("You are a commit message generator for a Jekyll blog.");
                systemPrompt.AppendLine(styleInstruction);
                systemPrompt.AppendLine(langInstruction);
                systemPrompt.AppendLine("Output ONLY the commit message text, nothing else — no markdown, no code fences, no explanation.");

                var userPrompt = $"Here are the file changes:\n\n{diffSummary}\n\nGenerate a commit message.";

                var request = new ChatRequest
                {
                    Model = profile.Model,
                    Messages = new List<ChatMessage>
                    {
                        new() { Role = "system", Content = systemPrompt.ToString() },
                        new() { Role = "user", Content = userPrompt }
                    },
                    Temperature = 0.7,
                    MaxTokens = 150
                };

                var (success, text, error) = await SendChatRequestAsync(profile.BaseUrl, decryptedKey, request);
                if (!success)
                    return (false, string.Empty, error);

                // Clean up the response
                var message = text.Trim()
                    .Trim('"')
                    .Trim('`')
                    .Trim();

                // Remove leading markdown code fence remnants
                if (message.StartsWith("commit", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = message.IndexOf('\n');
                    if (idx > 0)
                        message = message[(idx + 1)..].Trim();
                }

                return (true, message, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        // ── Helpers ─────────────────────────────────────────────

        private static async Task<(bool Success, string Text, string Error)> SendChatRequestAsync(
            string baseUrl, string decryptedKey, ChatRequest request)
        {
            var url = baseUrl.TrimEnd('/') + "/chat/completions";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            if (!string.IsNullOrWhiteSpace(decryptedKey))
                httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {decryptedKey}");

            using var response = await _httpClient.SendAsync(httpRequest);

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Try to extract error detail from response body
                string errorDetail;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    errorDetail = doc.RootElement.TryGetProperty("error", out var errElem) &&
                                  errElem.TryGetProperty("message", out var msgElem)
                        ? msgElem.GetString() ?? body
                        : body;
                }
                catch
                {
                    errorDetail = body;
                }

                return (false, string.Empty, $"{(int)response.StatusCode} {response.ReasonPhrase}: {errorDetail}");
            }

            try
            {
                var chatResponse = JsonSerializer.Deserialize<ChatResponse>(body);
                var text = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
                return (!string.IsNullOrWhiteSpace(text), text.Trim(), string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Failed to parse response: {ex.Message}");
            }
        }

        private static string ResolveLanguageInstruction(AiCommitLanguage language, string uiLanguage)
        {
            var target = language switch
            {
                AiCommitLanguage.Chinese => "zh",
                AiCommitLanguage.English => "en",
                AiCommitLanguage.FollowUI => uiLanguage.StartsWith("zh") ? "zh" : "en",
                _ => "en"
            };

            return target == "zh"
                ? "Write the commit message in Chinese (Simplified Chinese / 简体中文)."
                : "Write the commit message in English.";
        }
    }
}
