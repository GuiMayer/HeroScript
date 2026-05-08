using System;
using Core.Logging;

namespace Core.CLI
{
    /// <summary>
    /// Simple console logger implementation for CLI applications
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        public void LogDebug(string message)
        {
            Console.WriteLine($"[DEBUG] {message}");
        }

        public void LogInformation(string message)
        {
            Console.WriteLine($"[INFO] {message}");
        }

        public void LogWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] {message}");
            Console.ResetColor();
        }

        public void LogError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {message}");
            Console.ResetColor();
        }

        public void LogError(string message, Exception exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {message}");
            Console.WriteLine($"Exception: {exception.Message}");
            Console.WriteLine($"StackTrace: {exception.StackTrace}");
            Console.ResetColor();
        }
    }
}
