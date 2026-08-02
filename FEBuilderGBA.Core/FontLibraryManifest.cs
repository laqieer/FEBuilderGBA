// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FEBuilderGBA.Core
{
    public sealed class FontLibraryManifest
    {
        public int SchemaVersion { get; set; }
        public List<FontLibraryJobManifest> Jobs { get; } =
            new List<FontLibraryJobManifest>();
    }

    public sealed class FontLibraryJobManifest
    {
        public string Id { get; set; } = "";
        public string Locale { get; set; } = "";
        public FontLibraryFormat Format { get; set; }
        public List<FontLibraryStyle> Styles { get; } =
            new List<FontLibraryStyle>();
        public FontLibraryCorpusManifest Corpus { get; set; }
        public List<FontLibraryScalarRange> ScalarRanges { get; } =
            new List<FontLibraryScalarRange>();
        public FontLibraryFontManifest Font { get; set; }
        public FontLibraryLicenseManifest License { get; set; }
        public int VerticalOffset { get; set; }
        public FontLibraryMappingManifest Mapping { get; set; }
    }

    public sealed class FontLibraryCorpusManifest
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public List<int> Separators { get; } = new List<int>();
    }

    public sealed class FontLibraryScalarRange
    {
        public int Start { get; set; }
        public int End { get; set; }
    }

    public sealed class FontLibraryFontManifest
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public int ByteLength { get; set; }
        public string Family { get; set; } = "";
        public string Version { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public float Size { get; set; }
    }

    public sealed class FontLibraryLicenseManifest
    {
        public string LicenseId { get; set; } = "";
        public string LicenseFile { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string SourceUrl { get; set; } = "";
    }

    public enum FontLibraryMappingMode
    {
        Fixed,
        Range,
    }

    public sealed class FontLibraryMappingManifest
    {
        public FontLibraryMappingMode Mode { get; set; }
        public List<FontLibraryFixedMapping> Entries { get; } =
            new List<FontLibraryFixedMapping>();
        public int UnicodeStart { get; set; }
        public int UnicodeEnd { get; set; }
        public uint MojiStart { get; set; }
        public uint MojiEnd { get; set; }
    }

    public sealed class FontLibraryFixedMapping
    {
        public int Scalar { get; set; }
        public uint Moji { get; set; }
    }

    public sealed class FontLibraryManifestParseResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public FontLibraryManifest Manifest { get; set; }
        public FontLibraryFailureKind FailureKind { get; set; }
    }

    /// <summary>
    /// Strict schema-v1 parser. Every object has an exact property allowlist,
    /// duplicate names/comments/trailing commas are rejected, and all resource
    /// counts and spellings are bounded before planning.
    /// </summary>
    public static class FontLibraryManifestCore
    {
        public const int MaxManifestBytes = 1024 * 1024;
        public const int MaxJsonDepth = 32;
        public const int MaxJobs = 64;
        public const int MaxStylesPerJob = 2;
        public const int MaxScalarRangesPerJob = 4096;
        public const int MaxFixedMappingsPerJob = 65536;
        public const int MaxSeparatorsPerCorpus = 256;
        public const int MaxCorpusBytes = 16 * 1024 * 1024;
        public const int MaxFontBytes = 32 * 1024 * 1024;
        public const int MaxLicenseBytes = 16 * 1024 * 1024;
        public const long MaxInputBytesTotal = 256L * 1024 * 1024;
        public const int MaxRowsTotal = 65536;

        static readonly HashSet<string> RootNames =
            Names("schemaVersion", "jobs");
        static readonly HashSet<string> JobNames =
            Names("id", "locale", "format", "styles", "corpus",
                "scalarRanges", "font", "license", "verticalOffset",
                "mapping");
        static readonly HashSet<string> CorpusNames =
            Names("path", "sha256", "separators");
        static readonly HashSet<string> RangeNames =
            Names("start", "end");
        static readonly HashSet<string> FontNames =
            Names("path", "sha256", "byteLength", "family", "version",
                "sourceUrl", "size");
        static readonly HashSet<string> LicenseNames =
            Names("licenseId", "licenseFile", "sha256", "sourceUrl");
        static readonly HashSet<string> MappingNames =
            Names("mode", "entries", "unicodeStart", "unicodeEnd",
                "mojiStart", "mojiEnd");
        static readonly HashSet<string> FixedNames =
            Names("scalar", "moji");

        public static FontLibraryManifestParseResult Parse(byte[] utf8)
        {
            var result = new FontLibraryManifestParseResult();
            if (utf8 == null || utf8.Length == 0)
                return Fail(result, "Font-library manifest is empty.");
            if (utf8.Length > MaxManifestBytes)
                return Fail(result, "Font-library manifest exceeds the "
                    + MaxManifestBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte cap.");

            try
            {
                int offset = utf8.Length >= 3
                    && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF
                    ? 3 : 0;
                var strictUtf8 = new UTF8Encoding(false, true);
                string json = strictUtf8.GetString(
                    utf8, offset, utf8.Length - offset);
                return Parse(json);
            }
            catch (DecoderFallbackException ex)
            {
                return Fail(result,
                    "Manifest is not strict UTF-8: " + ex.Message);
            }
        }

        public static FontLibraryManifestParseResult Parse(string json)
        {
            var result = new FontLibraryManifestParseResult();
            if (json == null)
                return Fail(result, "Font-library manifest is null.");
            if (Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
                return Fail(result, "Font-library manifest exceeds the byte cap.");

            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    json,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = MaxJsonDepth,
                    });
                JsonElement root = document.RootElement;
                RequireKind(root, JsonValueKind.Object, "root");
                RejectDuplicateProperties(root, "root");
                RejectUnknown(root, RootNames, "root");
                int schema = RequireInt(root, "schemaVersion", "root");
                if (schema != 1)
                    throw Invalid(
                        "schemaVersion must be exactly 1.");

                JsonElement jobsElement =
                    Require(root, "jobs", JsonValueKind.Array, "root");
                int jobCount = jobsElement.GetArrayLength();
                if (jobCount < 1 || jobCount > MaxJobs)
                    throw Invalid("jobs count must be in 1.."
                        + MaxJobs.ToString(CultureInfo.InvariantCulture) + ".");

                var manifest = new FontLibraryManifest
                {
                    SchemaVersion = schema,
                };
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var portableIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int jobIndex = 0;
                foreach (JsonElement jobElement in jobsElement.EnumerateArray())
                {
                    string context = "jobs["
                        + jobIndex.ToString(CultureInfo.InvariantCulture) + "]";
                    FontLibraryJobManifest job = ParseJob(jobElement, context);
                    if (!ids.Add(job.Id))
                        throw Invalid("Duplicate job id '" + job.Id + "'.");
                    if (!portableIds.Add(job.Id))
                        throw Invalid(
                            "Job ids collide on a case-insensitive filesystem: '"
                            + job.Id + "'.");
                    manifest.Jobs.Add(job);
                    jobIndex++;
                }

                result.Success = true;
                result.Manifest = manifest;
                return result;
            }
            catch (Exception ex) when (
                ex is JsonException
                || ex is FormatException
                || ex is OverflowException
                || ex is FontLibraryManifestException)
            {
                return Fail(result, ex.Message);
            }
        }

        static FontLibraryJobManifest ParseJob(
            JsonElement element,
            string context)
        {
            RequireKind(element, JsonValueKind.Object, context);
            RejectDuplicateProperties(element, context);
            RejectUnknown(element, JobNames, context);

            string id = RequireString(element, "id", context);
            ValidateJobId(id);
            string locale = RequireString(element, "locale", context);
            if (locale.Length > 64 || HasControlOrWhitespace(locale))
                throw Invalid(context
                    + ".locale must be a non-empty whitespace-free locale tag.");

            string formatText = RequireString(
                element, "format", context);
            FontLibraryFormat format = formatText switch
            {
                "main-16x16" => FontLibraryFormat.Main16x16,
                "zh-16x13" => FontLibraryFormat.Zh16x13,
                _ => throw Invalid(context
                    + ".format must be 'main-16x16' or 'zh-16x13'."),
            };

            var job = new FontLibraryJobManifest
            {
                Id = id,
                Locale = locale,
                Format = format,
            };

            JsonElement styles =
                Require(element, "styles", JsonValueKind.Array, context);
            int styleCount = styles.GetArrayLength();
            if (styleCount < 1 || styleCount > MaxStylesPerJob)
                throw Invalid(context + ".styles must contain one or two styles.");
            var styleSet = new HashSet<FontLibraryStyle>();
            foreach (JsonElement styleElement in styles.EnumerateArray())
            {
                if (styleElement.ValueKind != JsonValueKind.String)
                    throw Invalid(context + ".styles entries must be strings.");
                string styleText = styleElement.GetString();
                FontLibraryStyle style = styleText switch
                {
                    "item" => FontLibraryStyle.Item,
                    "text" => FontLibraryStyle.Text,
                    _ => throw Invalid(context
                        + ".styles entries must be 'item' or 'text'."),
                };
                if (!styleSet.Add(style))
                    throw Invalid(context + ".styles contains a duplicate.");
                job.Styles.Add(style);
            }

            if (element.TryGetProperty("corpus", out JsonElement corpusElement))
            {
                RequireKind(corpusElement, JsonValueKind.Object,
                    context + ".corpus");
                RejectDuplicateProperties(corpusElement, context + ".corpus");
                RejectUnknown(corpusElement, CorpusNames, context + ".corpus");
                job.Corpus = new FontLibraryCorpusManifest
                {
                    Path = RequireRelativePath(
                        corpusElement, "path", context + ".corpus"),
                    Sha256 = RequireSha256(
                        corpusElement, "sha256", context + ".corpus"),
                };
                if (corpusElement.TryGetProperty(
                    "separators", out JsonElement separatorElement))
                {
                    RequireKind(separatorElement, JsonValueKind.Array,
                        context + ".corpus.separators");
                    if (separatorElement.GetArrayLength()
                        > MaxSeparatorsPerCorpus)
                        throw Invalid(context
                            + ".corpus.separators exceeds the cap.");
                    var separators = new HashSet<int>();
                    foreach (JsonElement separator
                        in separatorElement.EnumerateArray())
                    {
                        if (separator.ValueKind != JsonValueKind.String)
                            throw Invalid(context
                                + ".corpus.separators entries must be Unicode scalar strings.");
                        int scalar = ParseScalarText(
                            separator.GetString(),
                            context + ".corpus.separators");
                        if (!separators.Add(scalar))
                            throw Invalid(context
                                + ".corpus.separators contains a duplicate U+"
                                + scalar.ToString(
                                    "X4", CultureInfo.InvariantCulture)
                                + ".");
                        job.Corpus.Separators.Add(scalar);
                    }
                }
            }

            JsonElement ranges = Require(
                element, "scalarRanges", JsonValueKind.Array, context);
            if (ranges.GetArrayLength() > MaxScalarRangesPerJob)
                throw Invalid(context + ".scalarRanges exceeds the cap.");
            foreach (JsonElement rangeElement in ranges.EnumerateArray())
            {
                string rangeContext = context + ".scalarRanges";
                RequireKind(rangeElement, JsonValueKind.Object, rangeContext);
                RejectDuplicateProperties(rangeElement, rangeContext);
                RejectUnknown(rangeElement, RangeNames, rangeContext);
                int start = RequireScalar(rangeElement, "start", rangeContext);
                int end = RequireScalar(rangeElement, "end", rangeContext);
                if (end < start)
                    throw Invalid(rangeContext + " end is before start.");
                if (start <= 0xDFFF && end >= 0xD800)
                    throw Invalid(rangeContext
                        + " must not span Unicode surrogate code points.");
                job.ScalarRanges.Add(new FontLibraryScalarRange
                {
                    Start = start,
                    End = end,
                });
            }
            if (job.Corpus == null && job.ScalarRanges.Count == 0)
                throw Invalid(context
                    + " requires corpus and/or at least one scalar range.");

            JsonElement font =
                Require(element, "font", JsonValueKind.Object, context);
            RejectDuplicateProperties(font, context + ".font");
            RejectUnknown(font, FontNames, context + ".font");
            double size = RequireDouble(font, "size", context + ".font");
            float fontSize = (float)size;
            if (double.IsNaN(size) || double.IsInfinity(size)
                || size <= 0 || size > 256
                || float.IsNaN(fontSize) || float.IsInfinity(fontSize)
                || fontSize <= 0)
                throw Invalid(context + ".font.size must be in (0, 256].");
            job.Font = new FontLibraryFontManifest
            {
                Path = RequireRelativePath(font, "path", context + ".font"),
                Sha256 = RequireSha256(font, "sha256", context + ".font"),
                ByteLength = RequireInt(
                    font, "byteLength", context + ".font"),
                Family = RequireTrimmedString(
                    font, "family", context + ".font", 256),
                Version = RequireTrimmedString(
                    font, "version", context + ".font", 512),
                SourceUrl = RequireSourceUrl(
                    font, "sourceUrl", context + ".font"),
                Size = fontSize,
            };
            if (job.Font.ByteLength < 1
                || job.Font.ByteLength > MaxFontBytes)
                throw Invalid(context + ".font.byteLength must be in 1.."
                    + MaxFontBytes.ToString(CultureInfo.InvariantCulture)
                    + ".");

            JsonElement license =
                Require(element, "license", JsonValueKind.Object, context);
            RejectDuplicateProperties(license, context + ".license");
            RejectUnknown(license, LicenseNames, context + ".license");
            job.License = new FontLibraryLicenseManifest
            {
                LicenseId = RequireLicenseId(
                    license, "licenseId", context + ".license"),
                LicenseFile = RequireRelativePath(
                    license, "licenseFile", context + ".license"),
                Sha256 = RequireSha256(
                    license, "sha256", context + ".license"),
                SourceUrl = RequireSourceUrl(
                    license, "sourceUrl", context + ".license"),
            };

            job.VerticalOffset =
                RequireInt(element, "verticalOffset", context);
            if (job.VerticalOffset < -16 || job.VerticalOffset > 16)
                throw Invalid(context
                    + ".verticalOffset must be in -16..16.");

            job.Mapping = ParseMapping(
                Require(element, "mapping", JsonValueKind.Object, context),
                context + ".mapping");
            return job;
        }

        static FontLibraryMappingManifest ParseMapping(
            JsonElement element,
            string context)
        {
            RejectDuplicateProperties(element, context);
            RejectUnknown(element, MappingNames, context);
            string mode = RequireString(element, "mode", context);
            var mapping = new FontLibraryMappingManifest();
            if (mode == "fixed")
            {
                mapping.Mode = FontLibraryMappingMode.Fixed;
                RejectPresent(element, context,
                    "unicodeStart", "unicodeEnd", "mojiStart", "mojiEnd");
                JsonElement entries =
                    Require(element, "entries", JsonValueKind.Array, context);
                int count = entries.GetArrayLength();
                if (count < 1 || count > MaxFixedMappingsPerJob)
                    throw Invalid(context + ".entries count is outside 1.."
                        + MaxFixedMappingsPerJob.ToString(
                            CultureInfo.InvariantCulture) + ".");
                foreach (JsonElement entry in entries.EnumerateArray())
                {
                    RequireKind(entry, JsonValueKind.Object,
                        context + ".entries");
                    RejectDuplicateProperties(entry, context + ".entries");
                    RejectUnknown(entry, FixedNames, context + ".entries");
                    int scalar =
                        RequireScalar(entry, "scalar", context + ".entries");
                    uint moji =
                        RequireUInt(entry, "moji", context + ".entries");
                    mapping.Entries.Add(new FontLibraryFixedMapping
                    {
                        Scalar = scalar,
                        Moji = moji,
                    });
                }
            }
            else if (mode == "range")
            {
                mapping.Mode = FontLibraryMappingMode.Range;
                if (element.TryGetProperty("entries", out _))
                    throw Invalid(context
                        + ".entries is only valid for fixed mapping.");
                mapping.UnicodeStart =
                    RequireScalar(element, "unicodeStart", context);
                mapping.UnicodeEnd =
                    RequireScalar(element, "unicodeEnd", context);
                mapping.MojiStart =
                    RequireUInt(element, "mojiStart", context);
                mapping.MojiEnd =
                    RequireUInt(element, "mojiEnd", context);
                if (mapping.UnicodeEnd < mapping.UnicodeStart
                    || mapping.MojiEnd < mapping.MojiStart)
                    throw Invalid(context + " range bounds are reversed.");
            }
            else
            {
                throw Invalid(context + ".mode must be 'fixed' or 'range'.");
            }
            return mapping;
        }

        static void ValidateJobId(string id)
        {
            if (id.Length < 1 || id.Length > 64
                || id == "." || id == ".."
                || id.EndsWith(".", StringComparison.Ordinal))
                throw Invalid("Job id length/content is invalid.");
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool valid = c >= 'a' && c <= 'z'
                    || c >= 'A' && c <= 'Z'
                    || c >= '0' && c <= '9'
                    || c == '-' || c == '_' || c == '.';
                if (!valid)
                    throw Invalid(
                        "Job ids may contain only ASCII letters, digits, '.', '-' and '_'.");
            }
            char first = id[0];
            if (!((first >= 'a' && first <= 'z')
                    || (first >= 'A' && first <= 'Z')
                    || (first >= '0' && first <= '9')))
                throw Invalid("Job id must begin with an ASCII letter or digit.");
            string stem = id;
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem.Substring(0, dot);
            if (IsReservedPortableStem(stem))
                throw Invalid("Job id is a reserved portable filename.");
        }

        static string RequireRelativePath(
            JsonElement element,
            string name,
            string context)
        {
            string value = RequireNonEmptyString(
                element, name, context, 1024);
            if (value[0] == '/' || value[0] == '\\'
                || value.IndexOf('\\') >= 0
                || value.IndexOf(':') >= 0)
                throw Invalid(context + "." + name
                    + " must be a portable relative '/' path.");
            string[] parts = value.Split('/');
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == "." || part == "..")
                    throw Invalid(context + "." + name
                        + " contains an unsafe path segment.");
                if (part.EndsWith(".", StringComparison.Ordinal)
                    || part.EndsWith(" ", StringComparison.Ordinal)
                    || part.IndexOfAny(
                        new[] { '<', '>', '"', '|', '?', '*' }) >= 0)
                    throw Invalid(context + "." + name
                        + " contains a non-portable path segment.");
                string stem = part;
                int dot = stem.IndexOf('.');
                if (dot >= 0) stem = stem.Substring(0, dot);
                if (IsReservedPortableStem(stem))
                    throw Invalid(context + "." + name
                        + " contains a reserved filename.");
            }
            return value;
        }

        static bool IsReservedPortableStem(string stem)
        {
            string upper = (stem ?? "").ToUpperInvariant();
            return upper == "CON" || upper == "PRN" || upper == "AUX"
                || upper == "NUL"
                || (upper.Length == 4
                    && (upper.StartsWith("COM", StringComparison.Ordinal)
                        || upper.StartsWith("LPT", StringComparison.Ordinal))
                    && upper[3] >= '1' && upper[3] <= '9');
        }

        static string RequireSha256(
            JsonElement element,
            string name,
            string context)
        {
            string value = RequireString(element, name, context);
            if (value.Length != 64)
                throw Invalid(context + "." + name
                    + " must be 64 lowercase hexadecimal characters.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    throw Invalid(context + "." + name
                        + " must be canonical lowercase SHA-256.");
            }
            return value;
        }

        static int RequireScalar(
            JsonElement element,
            string name,
            string context)
        {
            string value = RequireString(element, name, context);
            return ParseScalarText(value, context + "." + name);
        }

        static int ParseScalarText(string value, string context)
        {
            if (!value.StartsWith("U+", StringComparison.Ordinal)
                || value.Length < 6 || value.Length > 8)
                throw Invalid(context
                    + " must use canonical U+XXXX spelling.");
            string hex = value.Substring(2);
            if (!int.TryParse(hex, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out int scalar)
                || !IsUnicodeScalar(scalar)
                || !string.Equals(value, "U+"
                    + scalar.ToString("X4", CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
                throw Invalid(context
                    + " is not a canonical Unicode scalar.");
            return scalar;
        }

        static string RequireTrimmedString(
            JsonElement element,
            string name,
            string context,
            int maxLength)
        {
            string text = RequireNonEmptyString(
                element, name, context, maxLength);
            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(text))
                throw Invalid(context + "." + name
                    + " must be non-whitespace and have no outer whitespace.");
            return text;
        }

        static string RequireSourceUrl(
            JsonElement element,
            string name,
            string context)
        {
            string text = RequireTrimmedString(
                element, name, context, 2048);
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri uri)
                || !string.Equals(
                    uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment))
                throw Invalid(context + "." + name
                    + " must be an absolute HTTPS URL without credentials or a fragment.");
            return text;
        }

        static string RequireLicenseId(
            JsonElement element,
            string name,
            string context)
        {
            string text = RequireTrimmedString(
                element, name, context, 128);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!((c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '.' || c == '-'))
                    throw Invalid(context + "." + name
                        + " must be a canonical license identifier.");
            }
            return text;
        }

        static uint RequireUInt(
            JsonElement element,
            string name,
            string context)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.Number, context);
            if (!value.TryGetUInt32(out uint result))
                throw Invalid(context + "." + name
                    + " must be an unsigned 32-bit integer.");
            return result;
        }

        static int RequireInt(
            JsonElement element,
            string name,
            string context)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.Number, context);
            if (!value.TryGetInt32(out int result))
                throw Invalid(context + "." + name
                    + " must be a 32-bit integer.");
            return result;
        }

        static double RequireDouble(
            JsonElement element,
            string name,
            string context)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.Number, context);
            if (!value.TryGetDouble(out double result))
                throw Invalid(context + "." + name
                    + " must be a finite number.");
            return result;
        }

        static string RequireString(
            JsonElement element,
            string name,
            string context)
            => RequireNonEmptyString(element, name, context, 4096);

        static string RequireNonEmptyString(
            JsonElement element,
            string name,
            string context,
            int maxLength)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.String, context);
            string text = value.GetString();
            if (string.IsNullOrEmpty(text) || text.Length > maxLength)
                throw Invalid(context + "." + name
                    + " must be a non-empty bounded string.");
            for (int i = 0; i < text.Length; i++)
                if (char.IsControl(text[i]))
                    throw Invalid(context + "." + name
                        + " contains a control character.");
            return text;
        }

        static JsonElement Require(
            JsonElement element,
            string name,
            JsonValueKind kind,
            string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                throw Invalid(context + " is missing required property '"
                    + name + "'.");
            if (value.ValueKind != kind)
                throw Invalid(context + "." + name + " must be "
                    + kind.ToString().ToLowerInvariant() + ".");
            return value;
        }

        static void RequireKind(
            JsonElement element,
            JsonValueKind kind,
            string context)
        {
            if (element.ValueKind != kind)
                throw Invalid(context + " must be "
                    + kind.ToString().ToLowerInvariant() + ".");
        }

        static void RejectUnknown(
            JsonElement element,
            HashSet<string> allowed,
            string context)
        {
            foreach (JsonProperty property in element.EnumerateObject())
                if (!allowed.Contains(property.Name))
                    throw Invalid(context + " contains unknown property '"
                        + property.Name + "'.");
        }

        static void RejectDuplicateProperties(
            JsonElement element,
            string context)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw Invalid(context + " contains duplicate property '"
                            + property.Name + "'.");
                    RejectDuplicateProperties(
                        property.Value, context + "." + property.Name);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    RejectDuplicateProperties(item, context + "["
                        + index.ToString(CultureInfo.InvariantCulture) + "]");
                    index++;
                }
            }
        }

        static void RejectPresent(
            JsonElement element,
            string context,
            params string[] names)
        {
            foreach (string name in names)
                if (element.TryGetProperty(name, out _))
                    throw Invalid(context + "." + name
                        + " is not valid for this mapping mode.");
        }

        static bool HasControlOrWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i]) || char.IsWhiteSpace(value[i]))
                    return true;
            return false;
        }

        internal static bool IsUnicodeScalar(int value)
            => value >= 0 && value <= 0x10FFFF
                && (value < 0xD800 || value > 0xDFFF);

        static HashSet<string> Names(params string[] names)
            => new HashSet<string>(names, StringComparer.Ordinal);

        static FontLibraryManifestParseResult Fail(
            FontLibraryManifestParseResult result,
            string error)
        {
            result.Error = "Invalid font-library manifest: " + error;
            result.FailureKind = FontLibraryFailureKind.Schema;
            return result;
        }

        static FontLibraryManifestException Invalid(string message)
            => new FontLibraryManifestException(message);

        sealed class FontLibraryManifestException : Exception
        {
            internal FontLibraryManifestException(string message)
                : base(message) { }
        }
    }
}
