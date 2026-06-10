using System;
using System.Text.Json;

namespace PhantomVault.Core.Services
{

    public static class JsonUtils
    {

        public static JsonDocument ParseStrict(string json, int maxDepth = 64, int maxSize = 1_048_576)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("Empty or null JSON input");

            if (System.Text.Encoding.UTF8.GetByteCount(json) > maxSize)
                throw new JsonException($"JSON exceeds maximum size of {maxSize} bytes");

            json = json.Trim();
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.Substring(1);

            var options = new JsonDocumentOptions
            {
                MaxDepth = maxDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            };

            return JsonDocument.Parse(json, options);
        }

        public static T DeserializeStrict<T>(string json, JsonSerializerOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("Empty or null JSON input");

            var strictOptions = options ?? new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                AllowTrailingCommas = false,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            };

            var result = JsonSerializer.Deserialize<T>(json, strictOptions);
            if (result == null)
                throw new JsonException($"Failed to deserialize JSON to type {typeof(T).Name}");

            return result;
        }

        public static bool TryParseRecovering(string json, out JsonDocument? doc, out string? error)
        {
            doc = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty input";
                return false;
            }

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

