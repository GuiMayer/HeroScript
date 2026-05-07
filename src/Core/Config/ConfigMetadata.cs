using System;
using System.Text.Json.Serialization;

namespace Core.Config
{
    /// <summary>
    /// Modelo para representar os metadados de uma configuração (config.json).
    /// Cada configuração é uma pasta independente com estrutura completa de Resources.
    /// </summary>
    public class ConfigMetadata
    {
        /// <summary>
        /// Nome da configuração (ex: "Alisyum", "Vampire Mod")
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Versão da configuração (ex: "1.0.0")
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Autor da configuração (ex: "core", "modder-name")
        /// </summary>
        [JsonPropertyName("author")]
        public string Author { get; set; } = "unknown";

        /// <summary>
        /// Descrição da configuração
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Config pai para herança delta (ex: "alisyum").
        /// Se null, esta é uma config base sem herança.
        /// </summary>
        [JsonPropertyName("parent")]
        public string? Parent { get; set; }

        /// <summary>
        /// URL do repositório Git (opcional, para Fase 5)
        /// </summary>
        [JsonPropertyName("git_repo")]
        public string? GitRepo { get; set; }

        /// <summary>
        /// Hash do commit Git (opcional, para Fase 5)
        /// </summary>
        [JsonPropertyName("git_hash")]
        public string? GitHash { get; set; }

        /// <summary>
        /// Data de criação da configuração (ISO 8601)
        /// </summary>
        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
}
