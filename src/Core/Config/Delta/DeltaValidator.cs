using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Config.Delta
{
    /// <summary>
    /// Resultado da validação de uma operação delta.
    /// </summary>
    public class DeltaValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public void AddError(string error) => Errors.Add(error);
        public void AddWarning(string warning) => Warnings.Add(warning);
    }

    /// <summary>
    /// Validador de operações delta.
    /// Verifica se uma DeltaDefinition tem todos os campos necessários
    /// e se os valores são válidos para a operação especificada.
    /// </summary>
    public static class DeltaValidator
    {
        /// <summary>
        /// Valida uma operação delta.
        /// </summary>
        /// <param name="resourceId">ID do recurso sendo modificado (para mensagens de erro)</param>
        /// <param name="delta">Definição delta a ser validada</param>
        /// <param name="strictMode">Se true, warnings são tratados como erros</param>
        public static DeltaValidationResult Validate(
            string resourceId,
            DeltaDefinition delta,
            bool strictMode = false)
        {
            var result = new DeltaValidationResult { IsValid = true };

            // Se não é delta estruturado, é formato legado (sempre válido)
            if (!delta.IsStructuredDelta())
            {
                result.AddWarning($"[{resourceId}] Using legacy format (no $delta specified) - assuming REPLACE");
                return result;
            }

            var operation = delta.GetOperationOrDefault();

            // Validar campos obrigatórios por operação
            switch (operation)
            {
                case DeltaOperationType.REPLACE:
                    ValidateReplace(resourceId, delta, result);
                    break;

                case DeltaOperationType.MERGE_SHALLOW:
                case DeltaOperationType.MERGE_DEEP:
                    ValidateMerge(resourceId, delta, result);
                    break;

                case DeltaOperationType.DELETE:
                    ValidateDelete(resourceId, delta, result);
                    break;

                case DeltaOperationType.ARRAY_APPEND:
                case DeltaOperationType.ARRAY_PREPEND:
                    ValidateArrayAppendPrepend(resourceId, delta, result);
                    break;

                case DeltaOperationType.ARRAY_REMOVE_INDEX:
                    ValidateArrayRemoveIndex(resourceId, delta, result);
                    break;

                case DeltaOperationType.ARRAY_REPLACE_INDEX:
                    ValidateArrayReplaceIndex(resourceId, delta, result);
                    break;

                case DeltaOperationType.FIELD_DELETE:
                    ValidateFieldDelete(resourceId, delta, result);
                    break;

                default:
                    result.AddError($"[{resourceId}] Unknown delta operation: {operation}");
                    result.IsValid = false;
                    break;
            }

            // Em modo strict, warnings são erros
            if (strictMode && result.Warnings.Any())
            {
                result.Errors.AddRange(result.Warnings);
                result.Warnings.Clear();
                result.IsValid = false;
            }

            return result;
        }

        private static void ValidateReplace(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // REPLACE precisa de Data ou Value
            if ((delta.Data == null || delta.Data.Count == 0) && !delta.Value.HasValue)
            {
                result.AddError($"[{resourceId}] REPLACE requires resource data or $value");
                result.IsValid = false;
            }

            // Avisar sobre campos desnecessários
            if (delta.TargetPath != null)
                result.AddWarning($"[{resourceId}] $target is ignored for REPLACE operation");
            if (delta.Index.HasValue)
                result.AddWarning($"[{resourceId}] $index is ignored for REPLACE operation");
        }

        private static void ValidateMerge(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // MERGE precisa de Data
            if (delta.Data == null || delta.Data.Count == 0)
            {
                result.AddError($"[{resourceId}] MERGE requires resource data to merge");
                result.IsValid = false;
            }

            // Avisar sobre campos desnecessários
            if (delta.Value.HasValue)
                result.AddWarning($"[{resourceId}] $value is ignored for MERGE operation");
            if (delta.Index.HasValue)
                result.AddWarning($"[{resourceId}] $index is ignored for MERGE operation");
        }

        private static void ValidateDelete(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // DELETE não precisa de nada além do $delta
            // Avisar sobre campos desnecessários
            if (delta.Data != null && delta.Data.Count > 0)
                result.AddWarning($"[{resourceId}] Resource data is ignored for DELETE operation");
            if (delta.Value.HasValue)
                result.AddWarning($"[{resourceId}] $value is ignored for DELETE operation");
            if (delta.TargetPath != null)
                result.AddWarning($"[{resourceId}] $target is ignored for DELETE operation");
            if (delta.Index.HasValue)
                result.AddWarning($"[{resourceId}] $index is ignored for DELETE operation");
        }

        private static void ValidateArrayAppendPrepend(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // ARRAY_APPEND/PREPEND precisa de $value
            if (!delta.Value.HasValue)
            {
                result.AddError($"[{resourceId}] ARRAY_APPEND/PREPEND requires $value with items to add");
                result.IsValid = false;
            }

            // $value deve ser um array
            if (delta.Value.HasValue && delta.Value.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                result.AddError($"[{resourceId}] $value must be an array for ARRAY_APPEND/PREPEND");
                result.IsValid = false;
            }

            // Avisar sobre campos desnecessários
            if (delta.Index.HasValue)
                result.AddWarning($"[{resourceId}] $index is ignored for ARRAY_APPEND/PREPEND");
        }

        private static void ValidateArrayRemoveIndex(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // ARRAY_REMOVE_INDEX precisa de $index
            if (!delta.Index.HasValue)
            {
                result.AddError($"[{resourceId}] ARRAY_REMOVE_INDEX requires $index");
                result.IsValid = false;
            }

            // $index não pode ser negativo
            if (delta.Index.HasValue && delta.Index.Value < 0)
            {
                result.AddError($"[{resourceId}] $index cannot be negative (got {delta.Index.Value})");
                result.IsValid = false;
            }

            // Avisar sobre campos desnecessários
            if (delta.Value.HasValue)
                result.AddWarning($"[{resourceId}] $value is ignored for ARRAY_REMOVE_INDEX");
        }

        private static void ValidateArrayReplaceIndex(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // ARRAY_REPLACE_INDEX precisa de $index e $value
            if (!delta.Index.HasValue)
            {
                result.AddError($"[{resourceId}] ARRAY_REPLACE_INDEX requires $index");
                result.IsValid = false;
            }

            if (!delta.Value.HasValue)
            {
                result.AddError($"[{resourceId}] ARRAY_REPLACE_INDEX requires $value");
                result.IsValid = false;
            }

            // $index não pode ser negativo
            if (delta.Index.HasValue && delta.Index.Value < 0)
            {
                result.AddError($"[{resourceId}] $index cannot be negative (got {delta.Index.Value})");
                result.IsValid = false;
            }
        }

        private static void ValidateFieldDelete(string resourceId, DeltaDefinition delta, DeltaValidationResult result)
        {
            // FIELD_DELETE precisa de $target
            if (string.IsNullOrWhiteSpace(delta.TargetPath))
            {
                result.AddError($"[{resourceId}] FIELD_DELETE requires $target specifying field path");
                result.IsValid = false;
            }

            // Validar formato do path (simples: "field" ou "nested.field")
            if (!string.IsNullOrWhiteSpace(delta.TargetPath))
            {
                // TODO: Implementar validação de JSONPath quando suportado (technical debt)
                // Por enquanto, apenas avisar sobre limitações
                if (delta.TargetPath.Contains('[') || delta.TargetPath.Contains(']'))
                {
                    result.AddWarning($"[{resourceId}] Array indexing in $target not supported in v1 (technical debt)");
                }
            }

            // Avisar sobre campos desnecessários
            if (delta.Value.HasValue)
                result.AddWarning($"[{resourceId}] $value is ignored for FIELD_DELETE");
            if (delta.Index.HasValue)
                result.AddWarning($"[{resourceId}] $index is ignored for FIELD_DELETE");
        }
    }
}
