using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Config.Delta
{
    /// <summary>
    /// Definição universal de uma operação delta.
    /// Representa como um mod modifica um recurso da config pai.
    /// Usa JsonElement para suportar qualquer tipo de dado JSON.
    /// </summary>
    public class DeltaDefinition
    {
        /// <summary>
        /// Tipo de operação delta a ser aplicada.
        /// Se não especificado, assume REPLACE (comportamento legado).
        /// </summary>
        [JsonPropertyName("$delta")]
        public DeltaOperationType? Operation { get; set; }

        /// <summary>
        /// Caminho do campo alvo para operações específicas.
        /// Formato: "field", "nested.field", "items[1]" ou "items[1].field".
        /// Usado por FIELD_DELETE.
        /// </summary>
        [JsonPropertyName("$target")]
        public string? TargetPath { get; set; }

        /// <summary>
        /// Valor a ser usado na operação delta.
        /// Usado por: ARRAY_APPEND, ARRAY_PREPEND, ARRAY_REPLACE_INDEX.
        /// </summary>
        [JsonPropertyName("$value")]
        public JsonElement? Value { get; set; }

        /// <summary>
        /// Índice do array para operações baseadas em índice.
        /// Usado por: ARRAY_REMOVE_INDEX, ARRAY_REPLACE_INDEX.
        /// </summary>
        [JsonPropertyName("$index")]
        public int? Index { get; set; }

        /// <summary>
        /// Dados do recurso (todos os campos que não são metadados delta).
        /// Contém os campos reais do recurso sendo modificado.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Data { get; set; }

        /// <summary>
        /// Verifica se esta definição é um delta estruturado ou formato legado.
        /// </summary>
        public bool IsStructuredDelta()
        {
            return Operation.HasValue || 
                   TargetPath != null || 
                   Value.HasValue || 
                   Index.HasValue;
        }

        /// <summary>
        /// Retorna o tipo de operação, usando REPLACE como padrão se não especificado.
        /// </summary>
        public DeltaOperationType GetOperationOrDefault()
        {
            return Operation ?? DeltaOperationType.REPLACE;
        }
    }
}
