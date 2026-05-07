using Core.Config;
using Core.CLI.Commands;
using Core.CLI;
using System;
using System.Linq;

namespace Core
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== HERO-ENGINE ===\n");

            // Parse comando
            if (args.Length > 0 && args[0] == "sync-resources")
            {
                ExecuteSyncResources(args.Skip(1).ToArray());
                return;
            }

            if (args.Length > 0 && args[0] == "test-resources")
            {
                ResourceLoadingTest.Execute();
                return;
            }

            if (args.Length > 0 && args[0] == "test-input-validation")
            {
                Console.WriteLine("Input validation tests have been migrated to xUnit.");
                Console.WriteLine("Run: dotnet test tests/Core.Tests/Core.Tests.csproj");
                return;
            }

            // Parse argumentos CLI
            string? configToLoad = null;
            bool listConfigs = false;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "--config" || arg == "-c")
                {
                    if (i + 1 < args.Length)
                    {
                        configToLoad = args[i + 1];
                        i++; // Skip next arg
                    }
                    else
                    {
                        Console.WriteLine("Error: --config requires a config name");
                        return;
                    }
                }
                else if (arg == "--list" || arg == "-l")
                {
                    listConfigs = true;
                }
                else if (arg == "--help" || arg == "-h")
                {
                    showHelp = true;
                }
            }

            // Mostrar ajuda
            if (showHelp)
            {
                ShowHelp();
                return;
            }

            // Listar configs disponíveis
            if (listConfigs)
            {
                ListAvailableConfigs();
                return;
            }

            // Carregar config especificada ou padrão
            if (configToLoad != null)
            {
                try
                {
                    ConfigManager.Instance.LoadConfig(configToLoad);
                    Console.WriteLine($"Config '{configToLoad}' loaded successfully\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading config '{configToLoad}': {ex.Message}");
                    return;
                }
            }
            else
            {
                // Carregar config padrão
                try
                {
                    ConfigManager.Instance.LoadConfig(ConfigManager.Instance.DefaultConfig);
                    Console.WriteLine($"Loaded default config: {ConfigManager.Instance.DefaultConfig}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading default config: {ex.Message}");
                    Console.WriteLine("Falling back to dev mode...\n");
                }
            }

            // Modo interativo (futuro)
            Console.WriteLine("Interactive mode not yet implemented.");
            Console.WriteLine("Use --help to see available options.");
            Console.WriteLine("\nPressione ENTER para fechar.");
            Console.ReadLine();
        }

        static void ExecuteSyncResources(string[] args)
        {
            var options = new SyncResourcesOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "--source")
                {
                    if (i + 1 < args.Length)
                    {
                        options.SourcePath = args[i + 1];
                        i++;
                    }
                }
                else if (arg == "--target")
                {
                    if (i + 1 < args.Length)
                    {
                        options.TargetPath = args[i + 1];
                        i++;
                    }
                }
                else if (arg == "--configs")
                {
                    if (i + 1 < args.Length)
                    {
                        options.Configs = args[i + 1].Split(',');
                        i++;
                    }
                }
                else if (arg == "--force")
                {
                    options.Force = true;
                }
            }

            var command = new SyncResourcesCommand();
            command.Execute(options);
        }

        static void ShowHelp()
        {
            Console.WriteLine("Hero-Engine - Sistema de configuração modular\n");
            Console.WriteLine("Uso: Core.exe [opções]\n");
            Console.WriteLine("Comandos:");
            Console.WriteLine("  sync-resources         Sincronizar recursos do projeto para user data");
            Console.WriteLine("\nOpções:");
            Console.WriteLine("  --config, -c <nome>    Carregar configuração específica");
            Console.WriteLine("  --list, -l             Listar configurações disponíveis");
            Console.WriteLine("  --help, -h             Mostrar esta ajuda\n");
            Console.WriteLine("Opções do sync-resources:");
            Console.WriteLine("  --source <path>        Caminho dos recursos fonte");
            Console.WriteLine("  --target <path>        Caminho de destino");
            Console.WriteLine("  --configs <list>       Configs para sincronizar (separadas por vírgula)");
            Console.WriteLine("  --force                Forçar sobrescrita\n");
            Console.WriteLine("Exemplos:");
            Console.WriteLine("  Core.CLI.exe --config alisyum");
            Console.WriteLine("  Core.CLI.exe --list");
            Console.WriteLine("  Core.CLI.exe sync-resources");
            Console.WriteLine("  Core.CLI.exe sync-resources --configs alisyum,test-orc --force");
            Console.WriteLine("\nPara executar testes, use: dotnet test");
        }

        static void ListAvailableConfigs()
        {
            Console.WriteLine("Configurações disponíveis:\n");

            var configs = ConfigManager.Instance.GetAvailableConfigs().ToList();

            if (configs.Count == 0)
            {
                Console.WriteLine("  (nenhuma configuração encontrada em user://)");
                Console.WriteLine($"  Caminho: {ConfigManager.Instance.GetUserDataPath()}");
                return;
            }

            foreach (var configName in configs)
            {
                var metadata = ConfigManager.Instance.GetConfigMetadata(configName);
                
                if (metadata != null)
                {
                    string parentInfo = metadata.Parent != null ? $" (herda de: {metadata.Parent})" : " (base)";
                    Console.WriteLine($"  • {configName}");
                    Console.WriteLine($"    Nome: {metadata.Name}");
                    Console.WriteLine($"    Versão: {metadata.Version}");
                    Console.WriteLine($"    Autor: {metadata.Author}");
                    Console.WriteLine($"    Descrição: {metadata.Description}");
                    Console.WriteLine($"    Herança: {parentInfo}");
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"  • {configName} (sem config.json)");
                }
            }

            Console.WriteLine($"Config padrão: {ConfigManager.Instance.DefaultConfig}");
        }
    }
}
