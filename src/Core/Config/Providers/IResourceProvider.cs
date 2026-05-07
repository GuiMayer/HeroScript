using System.IO;

namespace Core.Config.Providers
{
    /// <summary>
    /// Interface para provedores de recursos.
    /// Permite carregar recursos de múltiplas fontes (filesystem, embedded, etc).
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// Nome identificador do provider (para logging)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Prioridade do provider (maior = mais prioritário)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Verifica se um recurso existe sem abrir
        /// </summary>
        bool Exists(string relativePath);

        /// <summary>
        /// Abre um recurso para leitura
        /// </summary>
        /// <returns>Stream ou null se não existir</returns>
        Stream? OpenRead(string relativePath);

        /// <summary>
        /// Retorna caminho físico do recurso (para debugging)
        /// </summary>
        /// <returns>Caminho físico ou null se não aplicável</returns>
        string? GetPhysicalPath(string relativePath);
    }
}
