#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MeetingBar.Localization
{
    /// <summary>
    /// Translates English UI text through the JSON catalogs embedded next to this file.
    /// English text is the lookup key, so missing translations fall back to English.
    /// Shared by the tray app (.NET Framework), the WinUI settings app, and the Win+W widget provider.
    /// </summary>
    public static class Localizer
    {
        public sealed class LanguageOption
        {
            public LanguageOption(string tag, string nativeName) { Tag = tag; NativeName = nativeName; }
            public string Tag { get; }
            public string NativeName { get; }
        }

        public const string DefaultLanguage = "en";

        public static IReadOnlyList<LanguageOption> Languages { get; } = new[]
        {
            new LanguageOption("en", "English"),
            new LanguageOption("de", "Deutsch"),
            new LanguageOption("es", "Español"),
            new LanguageOption("fr", "Français"),
            new LanguageOption("it", "Italiano"),
            new LanguageOption("nl", "Nederlands"),
            new LanguageOption("pl", "Polski"),
            new LanguageOption("pt-BR", "Português (Brasil)"),
            new LanguageOption("tr", "Türkçe"),
            new LanguageOption("ru", "Русский"),
            new LanguageOption("ja", "日本語"),
            new LanguageOption("ko", "한국어"),
            new LanguageOption("zh-Hans", "简体中文"),
            new LanguageOption("zh-Hant", "繁體中文")
        };

        private static Dictionary<string, string> _strings = new Dictionary<string, string>();

        /// <summary>The resolved language tag, such as "de" or "zh-Hans".</summary>
        public static string Language { get; private set; } = DefaultLanguage;

        /// <summary>True when the user picked a language instead of following Windows.</summary>
        public static bool IsOverride { get; private set; }

        /// <summary>The specific culture for the UI language, used for xml:lang and font fallback.</summary>
        public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

        /// <summary>
        /// Culture for day and month names. Follows the chosen language when overridden and the
        /// Windows regional format otherwise.
        /// </summary>
        public static CultureInfo FormatCulture => IsOverride ? Culture : CultureInfo.CurrentCulture;

        public static event EventHandler LanguageChanged;

        /// <summary>Applies a language setting: a supported tag, or empty to follow the Windows display language.</summary>
        public static void SetLanguage(string setting)
        {
            var requested = Supported(setting);
            var language = requested ?? Supported(CultureInfo.CurrentUICulture) ?? DefaultLanguage;
            var isOverride = requested != null;
            if (language == Language && isOverride == IsOverride && (_strings.Count > 0 || language == DefaultLanguage)) return;
            _strings = Load(language);
            Language = language;
            IsOverride = isOverride;
            Culture = SpecificCulture(language);
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string T(string text) => text != null && _strings.TryGetValue(text, out var value) && !string.IsNullOrEmpty(value) ? value : text;

        public static string T(string format, params object[] args) => string.Format(FormatCulture, T(format), args);

        /// <summary>Maps a setting such as "de-DE" or "zh-TW" to a supported tag, or null when unsupported.</summary>
        public static string Supported(string setting)
        {
            if (string.IsNullOrWhiteSpace(setting)) return null;
            var exact = Languages.FirstOrDefault(x => string.Equals(x.Tag, setting.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.Tag;
            try { return Supported(CultureInfo.GetCultureInfo(setting.Trim())); }
            catch (CultureNotFoundException) { return null; }
        }

        private static string Supported(CultureInfo culture)
        {
            for (var current = culture; current != null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
            {
                var name = current.Name;
                if (name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
                if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
                if (name.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
                var match = Languages.FirstOrDefault(x => string.Equals(x.Tag, name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Tag;
                if (current.Parent == current) break;
            }
            return null;
        }

        private static CultureInfo SpecificCulture(string language)
        {
            try { return CultureInfo.CreateSpecificCulture(language); }
            catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en-US"); }
        }

        private static Dictionary<string, string> Load(string language)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (language == DefaultLanguage) return strings;
            try
            {
                using (var stream = typeof(Localizer).Assembly.GetManifestResourceStream("MeetingBar.Localization." + language + ".json"))
                {
                    if (stream == null) return strings;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        foreach (var pair in ParseCatalog(reader.ReadToEnd())) strings[pair.Key] = pair.Value;
                    }
                }
            }
            catch { }
            return strings;
        }

        /// <summary>Reads a flat JSON object of English text to translated text.</summary>
        public static IReadOnlyDictionary<string, string> ParseCatalog(string json)
        {
#if NET
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
#else
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            return serializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
#endif
        }
    }
}
