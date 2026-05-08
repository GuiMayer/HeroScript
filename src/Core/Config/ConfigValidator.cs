using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Core.Validation;

namespace Core.Config
{

    /// <summary>
    /// Validador de estrutura de configurações.
    /// Verifica que uma config tem todos os arquivos e metadados necessários.
    /// </summary>
    public class ConfigValidator
    {
        private readonly IConfigManager _configManager;

        public ConfigValidator(IConfigManager configManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        }

        /// <summary>
        /// Valida uma configuração e lança exceção se inválida
        /// </summary>
        public void ValidateConfig(string configName)
        {
            var result = ValidateConfigSafe(configName);
            
            if (!result.IsValid)
            {
                string errorMsg = $"Config '{configName}' validation failed:\n";
                foreach (var error in result.Errors)
                    errorMsg += $"  - {error}\n";
                
                throw new InvalidOperationException(errorMsg.TrimEnd());
            }

            // Logar warnings se houver
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine($"[ConfigValidator] Warnings for '{configName}':");
                foreach (var warning in result.Warnings)
                    Console.WriteLine($"  - {warning}");
            }
        }

        /// <summary>
        /// Valida uma configuração e retorna resultado sem lançar exceção
        /// </summary>
        public ValidationResult ValidateConfigSafe(string configName)
        {
            var result = new ValidationResult { IsValid = true };

            // 1. Verificar que pasta da config existe (user:// ou fallback dev)
            string configPath = _configManager.GetConfigPath(configName);
            bool configExists = Directory.Exists(configPath);

            // Fallback para dev (apenas se config for "dev" ou "alisyum" em modo dev)
            if (!configExists)
            {
                // Apenas fazer fallback se for config "dev" explícita ou config padrão em ambiente dev
                bool allowDevFallback = configName.Equals("dev", StringComparison.OrdinalIgnoreCase) ||
                                       configName.Equals(_configManager.DefaultConfig, StringComparison.OrdinalIgnoreCase);
                
                if (allowDevFallback)
                {
                    string devPath = Path.Combine(AppContext.BaseDirectory, "Resources");
                    if (Directory.Exists(devPath))
                    {
                        configPath = AppContext.BaseDirectory;
                        result.AddWarning($"Using dev fallback: {configPath}");
                    }
                    else
                    {
                        result.IsValid = false;
                        result.AddError($"Config directory not found: {configPath}");
                        return result;
                    }
                }
                else
                {
                    result.IsValid = false;
                    result.AddError($"Config directory not found: {configPath}");
                    return result;
                }
            }

            // 2. Verificar que config.json existe
            string configJsonPath = Path.Combine(configPath, "config.json");
            if (!File.Exists(configJsonPath))
            {
                // Em modo dev, config.json é opcional
                if (configPath == AppContext.BaseDirectory)
                {
                    result.AddWarning("config.json not found (dev mode - optional)");
                }
                else
                {
                    result.IsValid = false;
                    result.AddError("config.json not found");
                }
            }
            else
            {
                // 3. Validar que config.json é JSON válido
                try
                {
                    string jsonContent = File.ReadAllText(configJsonPath);
                    var metadata = JsonSerializer.Deserialize<ConfigMetadata>(jsonContent);

                    if (metadata == null)
                    {
                        result.IsValid = false;
                        result.AddError("config.json deserialization returned null");
                    }
                    else
                    {
                        // 4. Validar campos obrigatórios
                        if (string.IsNullOrWhiteSpace(metadata.Name))
                        {
                            result.IsValid = false;
                            result.AddError("config.json: 'name' field is required");
                        }

                        if (string.IsNullOrWhiteSpace(metadata.Version))
                        {
                            result.IsValid = false;
                            result.AddError("config.json: 'version' field is required");
                        }

                        if (string.IsNullOrWhiteSpace(metadata.Author))
                        {
                            result.IsValid = false;
                            result.AddError("config.json: 'author' field is required");
                        }

                        // 5. Validar que parent existe (se definido)
                        if (!string.IsNullOrWhiteSpace(metadata.Parent))
                        {
                            string parentPath = _configManager.GetConfigPath(metadata.Parent);
                            if (!Directory.Exists(parentPath))
                            {
                                result.IsValid = false;
                                result.AddError($"Parent config '{metadata.Parent}' not found at: {parentPath}");
                            }
                        }
                        else
                        {
                            result.AddWarning("Config has no parent - is this a base config?");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    result.IsValid = false;
                    result.AddError($"config.json is not valid JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    result.IsValid = false;
                    result.AddError($"Error reading config.json: {ex.Message}");
                }
            }

            // 6. Verificar que MathFormulas.json existe
            string mathFormulasPath = Path.Combine(configPath, "Resources", "Pipelines", "MathFormulas.json");
            if (!File.Exists(mathFormulasPath))
            {
                result.IsValid = false;
                result.AddError("Resources/Pipelines/MathFormulas.json not found");
            }
            else
            {
                // 7. Validar que MathFormulas.json é JSON válido
                try
                {
                    string jsonContent = File.ReadAllText(mathFormulasPath);
                    var formulas = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);

                    if (formulas == null || formulas.Count == 0)
                    {
                        result.AddWarning("MathFormulas.json is empty or invalid");
                    }
                }
                catch (JsonException ex)
                {
                    result.IsValid = false;
                    result.AddError($"MathFormulas.json is not valid JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    result.IsValid = false;
                    result.AddError($"Error reading MathFormulas.json: {ex.Message}");
                }
            }

            return result;
        }
    }
}
