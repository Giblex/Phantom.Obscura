using System;
using System.Text.Json;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// JSON parsing utilities with defensive and strict parsing modes.
    /// SECURITY: Use strict mode for untrusted input to prevent JSON injection attacks.
    /// </summary>
    public static class JsonUtils
    {
        /// <summary>
        /// Strict JSON parsing with security constraints.
        /// SECURITY: Rejects malformed JSON and enforces size limits.
        /// </summary>
        /// <param name="json">JSON string to parse</param>
        /// <param name="maxDepth">Maximum nesting depth (default: 64)</param>
        /// <param name="maxSize">Maximum JSON size in bytes (default: 1MB)</param>
        /// <returns>Parsed JsonDocument</returns>
        /// <exception cref="JsonException">If JSON is invalid or exceeds limits</exception>
        public static JsonDocument ParseStrict(string json, int maxDepth = 64, int maxSize = 1_048_576)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("Empty or null JSON input");

            // Check size limit
            if (System.Text.Encoding.UTF8.GetByteCount(json) > maxSize)
                throw new JsonException($"JSON exceeds maximum size of {maxSize} bytes");

            // Remove BOM if present (safe normalization)
            json = json.Trim();
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.Substring(1);

            var options = new JsonDocumentOptions
            {
                MaxDepth = maxDepth,
                AllowTrailingCommas = false, // Strict: no trailing commas
                CommentHandling = JsonCommentHandling.Disallow // Strict: no comments
            };

            return JsonDocument.Parse(json, options);
        }

        /// <summary>
        /// Deserializes JSON to a strongly-typed object with strict validation.
        /// SECURITY: Use this for policy files and security-critical data.
        /// </summary>
        public static T DeserializeStrict<T>(string json, JsonSerializerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("Empty or null JSON input");

            var strictOptions = options ?? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false, // Strict: case-sensitive
                AllowTrailingCommas = false,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            };

            var result = JsonSerializer.Deserialize<T>(json, strictOptions);
            if (result == null)
                throw new JsonException($"Failed to deserialize JSON to type {typeof(T).Name}");

            return result;
        }
        /// <summary>
        /// Try to parse JSON, recovering from common problems (BOM, leading
        /// garbage, extra text before/after the first JSON object/array).
        /// Returns true and a JsonDocument when successful; the caller
        /// is responsible for disposing the returned document.
        /// </summary>
        public static bool TryParseRecovering(string json, out JsonDocument? doc, out string? error)
        {
            doc = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty input";
                return false;
            }

            // Trim common whitespace and remove BOM if present
            json = json.Trim();
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.Substring(1);

            try
            {
                doc = JsonDocument.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                // Attempt to find the first JSON object {...}
                int start = json.IndexOf('{');
                if (start >= 0)
                {
                    int depth = 0;
                    for (int i = start; i < json.Length; i++)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                var candidate = json.Substring(start, i - start + 1);
                                try
                                {
                                    doc = JsonDocument.Parse(candidate);
                                    return true;
                                }
                                catch (JsonException ex)
                                {
                                    error = ex.Message;
                                    return false;
                                }
                            }
                        }
                    }
                    error = "Could not find matching closing brace for JSON object";
                    return false;
                }

                // Attempt array recovery
                int startArr = json.IndexOf('[');
                if (startArr >= 0)
                {
                    int depth2 = 0;
                    for (int i = startArr; i < json.Length; i++)
                    {
                        if (json[i] == '[') depth2++;
                        else if (json[i] == ']')
                        {
                            depth2--;
                            if (depth2 == 0)
                            {
                                var candidate = json.Substring(startArr, i - startArr + 1);
                                try
                                {
                                    doc = JsonDocument.Parse(candidate);
                                    return true;
                                }
                                catch (JsonException ex)
                                {
                                    error = ex.Message;
                                    return false;
                                }
                            }
                        }
                    }
                    error = "Could not find matching closing bracket for JSON array";
                    return false;
                }

                error = "Json parse failed and no recoverable object/array found";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
