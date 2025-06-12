using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using drupaltowp.Configuracion;

namespace drupaltowp.Services
{
    public class FileLogger
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new ();
        private readonly SemaphoreSlim _semaphore = new (1, 1);

        public FileLogger()
        {
            _logFilePath = ConfiguracionGeneral.LogFilePath;

            // Crear directorio si no existe
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Escribe un mensaje de log de forma síncrona
        /// </summary>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            lock (_lockObject)
            {
                var logEntry = FormatLogEntry(message, level);
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
        }

        /// <summary>
        /// Escribe un mensaje de log de forma asíncrona
        /// </summary>
        public async Task LogAsync(string message, LogLevel level = LogLevel.Info)
        {
            await _semaphore.WaitAsync();
            try
            {
                var logEntry = FormatLogEntry(message, level);
                await File.AppendAllTextAsync(_logFilePath, logEntry + Environment.NewLine);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Log de información
        /// </summary>
        public void LogInfo(string message) => Log(message, LogLevel.Info);
        public async Task LogInfoAsync(string message) => await LogAsync(message, LogLevel.Info);

        /// <summary>
        /// Log de advertencia
        /// </summary>
        public void LogWarning(string message) => Log(message, LogLevel.Warning);
        public async Task LogWarningAsync(string message) => await LogAsync(message, LogLevel.Warning);

        /// <summary>
        /// Log de error
        /// </summary>
        public void LogError(string message) => Log(message, LogLevel.Error);
        public async Task LogErrorAsync(string message) => await LogAsync(message, LogLevel.Error);

        /// <summary>
        /// Log de error con excepción
        /// </summary>
        public void LogError(string message, Exception ex)
        {
            var fullMessage = $"{message} - Exception: {ex.Message}";
            if (ex.InnerException != null)
            {
                fullMessage += $" - Inner: {ex.InnerException.Message}";
            }
            Log(fullMessage, LogLevel.Error);
        }

        public async Task LogErrorAsync(string message, Exception ex)
        {
            var fullMessage = $"{message} - Exception: {ex.Message}";
            if (ex.InnerException != null)
            {
                fullMessage += $" - Inner: {ex.InnerException.Message}";
            }
            await LogAsync(fullMessage, LogLevel.Error);
        }

        /// <summary>
        /// Log de debug (solo si está habilitado)
        /// </summary>
        public void LogDebug(string message)
        {
#if DEBUG
            Log(message, LogLevel.Debug);
#endif
        }

        public async Task LogDebugAsync(string message)
        {
#if DEBUG
            await LogAsync(message, LogLevel.Debug);
#endif
        }

        /// <summary>
        /// Limpia el archivo de log
        /// </summary>
        public void ClearLog()
        {
            lock (_lockObject)
            {
                if (File.Exists(_logFilePath))
                {
                    File.WriteAllText(_logFilePath, string.Empty);
                }
            }
        }

        /// <summary>
        /// Limpia el archivo de log de forma asíncrona
        /// </summary>
        public async Task ClearLogAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (File.Exists(_logFilePath))
                {
                    await File.WriteAllTextAsync(_logFilePath, string.Empty);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Escribe un separador en el log
        /// </summary>
        public void LogSeparator(string title = null)
        {
            var separator = new string('=', 80);
            if (!string.IsNullOrEmpty(title))
            {
                var titleLine = $"==== {title.ToUpper()} ====";
                var padding = (80 - titleLine.Length) / 2;
                if (padding > 0)
                {
                    titleLine = new string('=', padding) + $" {title.ToUpper()} " + new string('=', padding);
                }
                Log(separator, LogLevel.Info);
                Log(titleLine, LogLevel.Info);
                Log(separator, LogLevel.Info);
            }
            else
            {
                Log(separator, LogLevel.Info);
            }
        }

        /// <summary>
        /// Formatea la entrada del log
        /// </summary>
        private static string FormatLogEntry(string message, LogLevel level)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelText = GetLevelText(level);
            return $"[{timestamp}] [{levelText}] {message}";
        }

        /// <summary>
        /// Obtiene el texto del nivel de log
        /// </summary>
        private static string GetLevelText(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "INFO "
            };
        }

        /// <summary>
        /// Libera recursos
        /// </summary>
        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }

    /// <summary>
    /// Niveles de log
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}