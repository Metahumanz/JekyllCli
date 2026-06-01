using System.Collections.Generic;

namespace BlogTools.Models
{
    /// <summary>
    /// A named AI provider profile stored in app settings.
    /// </summary>
    public class AiCommitProfile
    {
        /// <summary>User-visible profile name (e.g. "OpenAI", "My DeepSeek").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Provider key used for preset lookup (e.g. "openai", "deepseek").</summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>Base URL for OpenAI-compatible chat completions.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>Optional URL to fetch available models (defaults to BaseUrl/models).</summary>
        public string ModelsUrl { get; set; } = string.Empty;

        /// <summary>The current model identifier.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>DPAPI-encrypted API key (Base64). Empty = no key.</summary>
        public string EncryptedKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Built-in provider presets. Values reflect official documentation as of 2026-06-01.
    /// These provide quick-start defaults; runtime model refresh takes precedence.
    /// </summary>
    public static class AiProviderPresets
    {
        public const string PresetOpenAI = "openai";
        public const string PresetDeepSeek = "deepseek";
        public const string PresetAliyun = "aliyun";
        public const string PresetMoonshot = "moonshot";
        public const string PresetZhipu = "zhipu";
        public const string PresetOpenRouter = "openrouter";
        public const string PresetSiliconFlow = "siliconflow";
        public const string PresetOllama = "ollama";
        public const string PresetLmStudio = "lmstudio";
        public const string PresetCustom = "custom";

        public static readonly Dictionary<string, (string Name, string BaseUrl, string DefaultModel, string ModelsUrl, List<string> SuggestedModels)> Presets = new()
        {
            [PresetOpenAI] = (
                "OpenAI",
                "https://api.openai.com/v1",
                "gpt-5.4-mini",
                "",
                new List<string> { "gpt-5.5", "gpt-5.4" }
            ),
            [PresetDeepSeek] = (
                "DeepSeek",
                "https://api.deepseek.com",
                "deepseek-v4-flash",
                "",
                new List<string> { "deepseek-v4-pro" }
            ),
            [PresetAliyun] = (
                "阿里百炼",
                "https://dashscope.aliyuncs.com/compatible-mode/v1",
                "qwen3.6-flash",
                "",
                new List<string> { "qwen3.6-plus", "qwen3.7-max" }
            ),
            [PresetMoonshot] = (
                "Moonshot / Kimi",
                "https://api.moonshot.cn/v1",
                "kimi-k2.6",
                "",
                new List<string>()
            ),
            [PresetZhipu] = (
                "智谱",
                "https://open.bigmodel.cn/api/paas/v4",
                "glm-5.1",
                "",
                new List<string>()
            ),
            [PresetOpenRouter] = (
                "OpenRouter",
                "https://openrouter.ai/api/v1",
                "",
                "",
                new List<string>()
            ),
            [PresetSiliconFlow] = (
                "硅基流动",
                "https://api.siliconflow.cn/v1",
                "",
                "",
                new List<string>()
            ),
            [PresetOllama] = (
                "Ollama",
                "http://127.0.0.1:11434/v1",
                "",
                "",
                new List<string>()
            ),
            [PresetLmStudio] = (
                "LM Studio",
                "http://127.0.0.1:1234/v1",
                "",
                "",
                new List<string>()
            ),
            [PresetCustom] = (
                "自定义",
                "",
                "",
                "",
                new List<string>()
            ),
        };

        /// <summary>
        /// DeepSeek old aliases that will be retired on 2026-07-24.
        /// </summary>
        public static readonly HashSet<string> DeepSeekDeprecatedModels = new()
        {
            "deepseek-chat",
            "deepseek-reasoner"
        };

        /// <summary>
        /// Providers for which we should NOT hardcode a default model
        /// (aggregators and local services where model lists change frequently).
        /// </summary>
        public static readonly HashSet<string> NoDefaultModelProviders = new()
        {
            PresetOpenRouter,
            PresetSiliconFlow,
            PresetOllama,
            PresetLmStudio,
            PresetCustom
        };

        /// <summary>
        /// Returns the effective Models URL for a profile.
        /// For SiliconFlow, appends ?type=text&amp;sub_type=chat to reduce noise.
        /// </summary>
        public static string GetEffectiveModelsUrl(AiCommitProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.ModelsUrl))
                return profile.ModelsUrl;

            var baseUrl = profile.BaseUrl?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;

            var url = $"{baseUrl}/models";

            if (profile.Provider == PresetSiliconFlow)
                url += "?type=text&sub_type=chat";

            return url;
        }
    }

    /// <summary>
    /// Commit style options.
    /// </summary>
    public enum AiCommitStyle
    {
        /// <summary>A plain single-line summary.</summary>
        SingleLine,
        /// <summary>Conventional Commit format (e.g. "feat: add ...").</summary>
        ConventionalCommit
    }

    /// <summary>
    /// Output language preference for AI-generated commit messages.
    /// </summary>
    public enum AiCommitLanguage
    {
        FollowUI,
        Chinese,
        English
    }

    /// <summary>
    /// Behavior after the first successful AI commit message generation.
    /// </summary>
    public enum AiCommitBehavior
    {
        /// <summary>Commit immediately without confirmation.</summary>
        DirectCommit,
        /// <summary>Show confirmation dialog with editable message each time.</summary>
        ConfirmAndEdit
    }
}
