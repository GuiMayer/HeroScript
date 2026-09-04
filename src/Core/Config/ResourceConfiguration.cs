namespace Core.Config
{
    /// <summary>
    /// Configuração do sistema de carregamento de recursos
    /// </summary>
    public class ResourceConfiguration
    {
        /// <summary>
        /// Modo de operação (auto-detecta por padrão)
        /// </summary>
        public ResourceMode Mode { get; set; } = ResourceMode.Auto;

        /// <summary>
        /// Caminho customizado para recursos core (opcional)
        /// </summary>
        public string? CoreResourcesPath { get; set; }

        /// <summary>
        /// Validar versões de recursos
        /// </summary>
        public bool ValidateResourceVersions { get; set; } = false;
    }

    /// <summary>
    /// Modo de operação do sistema de recursos
    /// </summary>
    public enum ResourceMode
    {
        /// <summary>
        /// Detecta automaticamente (dev vs production)
        /// </summary>
        Auto,

        /// <summary>
        /// Modo desenvolvimento (carrega direto do projeto)
        /// </summary>
        Development,

        /// <summary>
        /// Modo produção (carrega de AppData com fallback)
        /// </summary>
        Production
    }
}
