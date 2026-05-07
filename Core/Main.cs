using Core.Tests;
using Core.Config;
using System;
using System.Linq;

namespace Core
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== HERO-ENGINE ===\n");

            // Parse argumentos CLI
            string? configToLoad = null;
            bool runTests = false;
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
                else if (arg == "--test" || arg == "-t")
                {
                    runTests = true;
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
                    ConfigManager.LoadConfig(configToLoad);
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
                    ConfigManager.LoadConfig(ConfigManager.DefaultConfig);
                    Console.WriteLine($"Loaded default config: {ConfigManager.DefaultConfig}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading default config: {ex.Message}");
                    Console.WriteLine("Falling back to dev mode...\n");
                }
            }

            // Executar testes se solicitado
            if (runTests)
            {
                MathEngineTests.RunAllTests();
                ConfigTests.RunAllTests();
                Console.WriteLine("\n[DEBUG]: Testes concluídos. Pressione ENTER para fechar.");
                Console.ReadLine();
                return;
            }

            // Modo interativo (futuro)
            Console.WriteLine("Interactive mode not yet implemented.");
            Console.WriteLine("Use --help to see available options.");
            Console.WriteLine("\nPressione ENTER para fechar.");
            Console.ReadLine();
        }

        static void ShowHelp()
        {
            Console.WriteLine("Hero-Engine - Sistema de configuração modular\n");
            Console.WriteLine("Uso: Core.exe [opções]\n");
            Console.WriteLine("Opções:");
            Console.WriteLine("  --config, -c <nome>    Carregar configuração específica");
            Console.WriteLine("  --list, -l             Listar configurações disponíveis");
            Console.WriteLine("  --test, -t             Executar testes");
            Console.WriteLine("  --help, -h             Mostrar esta ajuda\n");
            Console.WriteLine("Exemplos:");
            Console.WriteLine("  Core.exe --config alisyum");
            Console.WriteLine("  Core.exe --config test-orc-mod --test");
            Console.WriteLine("  Core.exe --list");
        }

        static void ListAvailableConfigs()
        {
            Console.WriteLine("Configurações disponíveis:\n");

            var configs = ConfigManager.GetAvailableConfigs().ToList();

            if (configs.Count == 0)
            {
                Console.WriteLine("  (nenhuma configuração encontrada em user://)");
                Console.WriteLine($"  Caminho: {ConfigManager.GetUserDataPath()}");
                return;
            }

            foreach (var configName in configs)
            {
                var metadata = ConfigManager.GetConfigMetadata(configName);
                
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

            Console.WriteLine($"Config padrão: {ConfigManager.DefaultConfig}");
        }
    }
}
