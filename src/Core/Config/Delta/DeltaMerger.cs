using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Logging;

namespace Core.Config.Delta
{
    /// <summary>
    /// Engine de merge de operações delta.
    /// Aplica transformações delta em recursos JSON de forma genérica.
    /// </summary>
    public static class DeltaMerger
    {
        /// <summary>
        /// Aplica uma operação delta em um recurso base.
        /// </summary>
        /// <param name="baseValue">Valor base do recurso (pode ser null para recursos novos)</param>
        /// <param name="delta">Definição da operação delta</param>
        /// <param name="resourceId">ID do recurso (para mensagens de erro)</param>
        /// <param name="strictMode">Se true, erros causam exceções; se false, retorna baseValue</param>
        /// <returns>Recurso merged ou null se DELETE</returns>
        public static JsonElement? ApplyDelta(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode = false,
            ILogger? logger = null)
        {
            logger ??= NullLogger.Instance;
            var operation = delta.GetOperationOrDefault();

            try
            {
                switch (operation)
                {
                    case DeltaOperationType.REPLACE:
                        return ApplyReplace(delta);

                    case DeltaOperationType.MERGE_SHALLOW:
                        return ApplyMergeShallow(baseValue, delta, resourceId, strictMode, logger);

                    case DeltaOperationType.MERGE_DEEP:
                        return ApplyMergeDeep(baseValue, delta, resourceId, strictMode, logger);

                    case DeltaOperationType.DELETE:
                        return null; // Recurso deletado

                    case DeltaOperationType.ARRAY_APPEND:
                        return ApplyArrayAppend(baseValue, delta, resourceId, strictMode, logger);

                    case DeltaOperationType.ARRAY_PREPEND:
                        return ApplyArrayPrepend(baseValue, delta, resourceId, strictMode, logger);

                    case DeltaOperationType.ARRAY_REMOVE_INDEX:
                        return ApplyArrayRemoveIndex(baseValue, delta, resourceId, strictMode, logger);

                    case DeltaOperationType.ARRAY_REPLACE_INDEX:
                        return ApplyArrayReplaceIndex(baseValue, delta, resourceId, strictMode, logger);

                    case DeltaOperationType.FIELD_DELETE:
                        return ApplyFieldDelete(baseValue, delta, resourceId, strictMode, logger);

                    default:
                        throw new InvalidOperationException($"Unknown delta operation: {operation}");
                }
            }
            catch (Exception ex)
            {
                if (strictMode)
                    throw new InvalidOperationException($"[{resourceId}] Delta merge failed: {ex.Message}", ex);

                logger.LogWarning($"Failed to apply {operation} to '{resourceId}': {ex.Message}");
                return baseValue; // Retorna valor original em caso de erro
            }
        }

        private static JsonElement ApplyReplace(DeltaDefinition delta)
        {
            // Se tem $value, usa ele; senão usa Data
            if (delta.Value.HasValue)
                return delta.Value.Value;

            // Converter Data para JsonElement
            if (delta.Data != null && delta.Data.Count > 0)
            {
                var json = JsonSerializer.Serialize(delta.Data);
                return JsonDocument.Parse(json).RootElement.Clone();
            }

            throw new InvalidOperationException("REPLACE requires $value or resource data");
        }

        private static JsonElement ApplyMergeShallow(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
            {
                // Sem base, MERGE vira REPLACE
                logger.LogWarning($"No base value for '{resourceId}', treating MERGE_SHALLOW as REPLACE");
                return ApplyReplace(delta);
            }

            if (baseValue.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"MERGE_SHALLOW requires base to be an object, got {baseValue.Value.ValueKind}");

            if (delta.Data == null || delta.Data.Count == 0)
                throw new InvalidOperationException("MERGE_SHALLOW requires resource data");

            // Merge shallow: campos top-level são mesclados, nested objects são substituídos
            var merged = new Dictionary<string, JsonElement>();

            // Copiar todos os campos da base
            foreach (var prop in baseValue.Value.EnumerateObject())
            {
                merged[prop.Name] = prop.Value.Clone();
            }

            // Sobrescrever com campos do delta
            foreach (var kvp in delta.Data)
            {
                merged[kvp.Key] = kvp.Value.Clone();
            }

            // Converter de volta para JsonElement
            var json = JsonSerializer.Serialize(merged);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement ApplyMergeDeep(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
            {
                // Sem base, MERGE vira REPLACE
                logger.LogWarning($"No base value for '{resourceId}', treating MERGE_DEEP as REPLACE");
                return ApplyReplace(delta);
            }

            if (baseValue.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"MERGE_DEEP requires base to be an object, got {baseValue.Value.ValueKind}");

            if (delta.Data == null || delta.Data.Count == 0)
                throw new InvalidOperationException("MERGE_DEEP requires resource data");

            // Merge deep recursivo
            var deltaElement = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(delta.Data));
            var merged = MergeDeepRecursive(baseValue.Value, deltaElement);

            return merged;
        }

        private static JsonElement MergeDeepRecursive(JsonElement baseElement, JsonElement deltaElement)
        {
            // Se delta não é objeto, substitui inteiro (inclui arrays)
            if (deltaElement.ValueKind != JsonValueKind.Object)
                return deltaElement.Clone();

            // Se base não é objeto, substitui inteiro
            if (baseElement.ValueKind != JsonValueKind.Object)
                return deltaElement.Clone();

            // Ambos são objetos: merge recursivo
            var merged = new Dictionary<string, JsonElement>();

            // Copiar todos os campos da base
            foreach (var prop in baseElement.EnumerateObject())
            {
                merged[prop.Name] = prop.Value.Clone();
            }

            // Merge campos do delta
            foreach (var prop in deltaElement.EnumerateObject())
            {
                if (merged.ContainsKey(prop.Name))
                {
                    // Campo existe: merge recursivo
                    merged[prop.Name] = MergeDeepRecursive(merged[prop.Name], prop.Value);
                }
                else
                {
                    // Campo novo: adiciona
                    merged[prop.Name] = prop.Value.Clone();
                }
            }

            // Converter de volta para JsonElement
            var json = JsonSerializer.Serialize(merged);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement ApplyArrayAppend(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
                throw new InvalidOperationException($"ARRAY_APPEND requires base value for '{resourceId}'");

            if (!delta.Value.HasValue)
                throw new InvalidOperationException("ARRAY_APPEND requires $value");

            if (delta.Value.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("ARRAY_APPEND $value must be an array");

            // Se base não é array, trata como array vazio
            List<JsonElement> baseArray;
            if (baseValue.Value.ValueKind == JsonValueKind.Array)
            {
                baseArray = baseValue.Value.EnumerateArray().Select(e => e.Clone()).ToList();
            }
            else
            {
                logger.LogWarning($"Base value for '{resourceId}' is not an array, treating as empty array");
                baseArray = new List<JsonElement>();
            }

            // Adicionar itens do delta ao final
            foreach (var item in delta.Value.Value.EnumerateArray())
            {
                baseArray.Add(item.Clone());
            }

            // Converter de volta para JsonElement
            var json = JsonSerializer.Serialize(baseArray);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement ApplyArrayPrepend(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
                throw new InvalidOperationException($"ARRAY_PREPEND requires base value for '{resourceId}'");

            if (!delta.Value.HasValue)
                throw new InvalidOperationException("ARRAY_PREPEND requires $value");

            if (delta.Value.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("ARRAY_PREPEND $value must be an array");

            // Se base não é array, trata como array vazio
            List<JsonElement> baseArray;
            if (baseValue.Value.ValueKind == JsonValueKind.Array)
            {
                baseArray = baseValue.Value.EnumerateArray().Select(e => e.Clone()).ToList();
            }
            else
            {
                logger.LogWarning($"Base value for '{resourceId}' is not an array, treating as empty array");
                baseArray = new List<JsonElement>();
            }

            // Adicionar itens do delta ao início
            var deltaItems = delta.Value.Value.EnumerateArray().Select(e => e.Clone()).ToList();
            deltaItems.AddRange(baseArray);

            // Converter de volta para JsonElement
            var json = JsonSerializer.Serialize(deltaItems);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement ApplyArrayRemoveIndex(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
                throw new InvalidOperationException($"ARRAY_REMOVE_INDEX requires base value for '{resourceId}'");

            if (!delta.Index.HasValue)
                throw new InvalidOperationException("ARRAY_REMOVE_INDEX requires $index");

            if (baseValue.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"ARRAY_REMOVE_INDEX requires base to be an array, got {baseValue.Value.ValueKind}");

            var baseArray = baseValue.Value.EnumerateArray().Select(e => e.Clone()).ToList();

            if (delta.Index.Value < 0 || delta.Index.Value >= baseArray.Count)
            {
                var msg = $"Index {delta.Index.Value} out of range [0, {baseArray.Count - 1}]";
                if (strictMode)
                    throw new IndexOutOfRangeException(msg);
                
                logger.LogWarning($"Index {delta.Index.Value} out of range [0, {baseArray.Count - 1}] for '{resourceId}', ignoring operation");
                return baseValue.Value;
            }

            // Remover item
            baseArray.RemoveAt(delta.Index.Value);

            // Converter de volta para JsonElement
            var json = JsonSerializer.Serialize(baseArray);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement ApplyArrayReplaceIndex(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
                throw new InvalidOperationException($"ARRAY_REPLACE_INDEX requires base value for '{resourceId}'");

            if (!delta.Index.HasValue)
                throw new InvalidOperationException("ARRAY_REPLACE_INDEX requires $index");

            if (!delta.Value.HasValue)
                throw new InvalidOperationException("ARRAY_REPLACE_INDEX requires $value");

            if (baseValue.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"ARRAY_REPLACE_INDEX requires base to be an array, got {baseValue.Value.ValueKind}");

            var baseArray = baseValue.Value.EnumerateArray().Select(e => e.Clone()).ToList();

            if (delta.Index.Value < 0 || delta.Index.Value >= baseArray.Count)
            {
                var msg = $"Index {delta.Index.Value} out of range [0, {baseArray.Count - 1}]";
                if (strictMode)
                    throw new IndexOutOfRangeException(msg);
                
                logger.LogWarning($"Index {delta.Index.Value} out of range [0, {baseArray.Count - 1}] for '{resourceId}', ignoring operation");
                return baseValue.Value;
            }

            // Substituir item
            baseArray[delta.Index.Value] = delta.Value.Value.Clone();

            // Converter de volta para JsonElement
            var json = JsonSerializer.Serialize(baseArray);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement ApplyFieldDelete(
            JsonElement? baseValue,
            DeltaDefinition delta,
            string resourceId,
            bool strictMode,
            ILogger logger)
        {
            if (!baseValue.HasValue)
                throw new InvalidOperationException($"FIELD_DELETE requires base value for '{resourceId}'");

            if (string.IsNullOrWhiteSpace(delta.TargetPath))
                throw new InvalidOperationException("FIELD_DELETE requires $target");

            if (baseValue.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"FIELD_DELETE requires base to be an object, got {baseValue.Value.ValueKind}");

            if (!DeltaTargetPath.TryParse(delta.TargetPath, out var segments, out var pathError))
                throw new InvalidOperationException(pathError);

            var root = JsonNode.Parse(baseValue.Value.GetRawText())
                ?? throw new InvalidOperationException("Failed to deserialize base value");
            if (!DeltaTargetPath.TryDelete(root, segments, out var deleteError))
            {
                if (strictMode)
                    throw new KeyNotFoundException(deleteError);

                logger.LogWarning($"{deleteError} in '{resourceId}', ignoring operation");
                return baseValue.Value;
            }

            return JsonSerializer.SerializeToElement(root);
        }
    }
}
