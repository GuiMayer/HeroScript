using System.Text.Json.Serialization;

namespace Core.Config.Delta
{
    /// <summary>
    /// Tipos de operações delta suportadas pelo ResourceLoader.
    /// Define como um mod pode modificar recursos da config pai.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeltaOperationType
    {
        /// <summary>
        /// Substitui o recurso inteiro (comportamento padrão/legado).
        /// Usado quando o mod fornece uma versão completamente nova do recurso.
        /// </summary>
        REPLACE,

        /// <summary>
        /// Merge superficial - apenas campos de primeiro nível são mesclados.
        /// Campos nested objects são substituídos inteiros.
        /// Arrays são substituídos inteiros.
        /// </summary>
        MERGE_SHALLOW,

        /// <summary>
        /// Merge profundo recursivo - mescla campos em todos os níveis.
        /// Preserva campos não mencionados no delta.
        /// Arrays são substituídos inteiros (use operações específicas para modificar arrays).
        /// </summary>
        MERGE_DEEP,

        /// <summary>
        /// Remove o recurso completamente da config final.
        /// Útil para desabilitar recursos da config pai.
        /// </summary>
        DELETE,

        /// <summary>
        /// Adiciona itens ao final de um array existente.
        /// Requer que o recurso base tenha um array no caminho especificado.
        /// </summary>
        ARRAY_APPEND,

        /// <summary>
        /// Adiciona itens ao início de um array existente.
        /// Requer que o recurso base tenha um array no caminho especificado.
        /// </summary>
        ARRAY_PREPEND,

        /// <summary>
        /// Remove item de um array por índice.
        /// Requer campo $index especificando qual item remover.
        /// </summary>
        ARRAY_REMOVE_INDEX,

        /// <summary>
        /// Substitui item de um array por índice.
        /// Requer campo $index especificando qual item substituir.
        /// Requer campo $value com o novo valor.
        /// </summary>
        ARRAY_REPLACE_INDEX,

        /// <summary>
        /// Remove um campo específico do recurso.
        /// Requer campo $target especificando uma propriedade ou item de array
        /// (ex: "params.SCALING_VALUE" ou "effects[1]").
        /// </summary>
        FIELD_DELETE
    }
}
