using System;
using System.Threading;
using System.Threading.Tasks;

namespace drupaltowp.Services
{
    /// <summary>
    /// Servicio centralizado para manejo de cancelación de operaciones
    /// </summary>
    public class CancellationService
    {
        private CancellationTokenSource _currentOperationTokenSource;
        private readonly object _lock = new object();

        public event EventHandler<string> OperationCancelled;
        public event EventHandler<string> OperationStarted;
        public event EventHandler<string> OperationCompleted;

        /// <summary>
        /// Indica si hay una operación en progreso
        /// </summary>
        public bool IsOperationInProgress { get; private set; }

        /// <summary>
        /// Nombre de la operación actual
        /// </summary>
        public string CurrentOperationName { get; private set; }

        /// <summary>
        /// Token de la operación actual
        /// </summary>
        public CancellationToken CurrentToken => _currentOperationTokenSource?.Token ?? CancellationToken.None;

        /// <summary>
        /// Inicia una nueva operación cancelable
        /// </summary>
        /// <param name="operationName">Nombre descriptivo de la operación</param>
        /// <param name="timeoutMinutes">Timeout en minutos (opcional)</param>
        /// <returns>Token de cancelación para la operación</returns>
        public CancellationToken StartOperation(string operationName, int? timeoutMinutes = null)
        {
            lock (_lock)
            {
                // Cancelar operación anterior si existe
                if (_currentOperationTokenSource != null)
                {
                    _currentOperationTokenSource.Cancel();
                    _currentOperationTokenSource.Dispose();
                }

                // Crear nuevo token
                _currentOperationTokenSource = timeoutMinutes.HasValue
                    ? new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes.Value))
                    : new CancellationTokenSource();

                IsOperationInProgress = true;
                CurrentOperationName = operationName;

                // Configurar callback para cuando se cancele
                _currentOperationTokenSource.Token.Register(() =>
                {
                    lock (_lock)
                    {
                        IsOperationInProgress = false;
                        OperationCancelled?.Invoke(this, CurrentOperationName);
                        CurrentOperationName = null;
                    }
                });

                OperationStarted?.Invoke(this, operationName);
                return _currentOperationTokenSource.Token;
            }
        }

        /// <summary>
        /// Cancela la operación actual
        /// </summary>
        public void CancelCurrentOperation()
        {
            lock (_lock)
            {
                if (_currentOperationTokenSource != null && !_currentOperationTokenSource.Token.IsCancellationRequested)
                {
                    _currentOperationTokenSource.Cancel();
                }
            }
        }

        /// <summary>
        /// Marca la operación actual como completada exitosamente
        /// </summary>
        public void CompleteCurrentOperation()
        {
            lock (_lock)
            {
                if (IsOperationInProgress)
                {
                    var operationName = CurrentOperationName;
                    IsOperationInProgress = false;
                    CurrentOperationName = null;

                    OperationCompleted?.Invoke(this, operationName);

                    _currentOperationTokenSource?.Dispose();
                    _currentOperationTokenSource = null;
                }
            }
        }

        /// <summary>
        /// Verifica si la operación fue cancelada y lanza excepción si es necesario
        /// </summary>
        /// <param name="token">Token a verificar</param>
        /// <param name="message">Mensaje personalizado para la excepción</param>
        public static void ThrowIfCancellationRequested(CancellationToken token, string message = null)
        {
            if (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(message ?? "La operación fue cancelada por el usuario", token);
            }
        }

        /// <summary>
        /// Ejecuta una operación con manejo automático de cancelación
        /// </summary>
        /// <param name="operationName">Nombre de la operación</param>
        /// <param name="operation">Función a ejecutar</param>
        /// <param name="timeoutMinutes">Timeout opcional</param>
        /// <returns>Resultado de la operación</returns>
        public async Task<T> ExecuteOperationAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            int? timeoutMinutes = null)
        {
            var token = StartOperation(operationName, timeoutMinutes);

            try
            {
                var result = await operation(token);
                CompleteCurrentOperation();
                return result;
            }
            catch (OperationCanceledException)
            {
                // Ya manejado por el token
                throw;
            }
            catch (Exception)
            {
                // Marcar como completada (con error)
                CompleteCurrentOperation();
                throw;
            }
        }

        /// <summary>
        /// Sobrecarga para operaciones sin resultado
        /// </summary>
        public async Task ExecuteOperationAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            int? timeoutMinutes = null)
        {
            await ExecuteOperationAsync(operationName, async token =>
            {
                await operation(token);
                return true; // Dummy return
            }, timeoutMinutes);
        }

        /// <summary>
        /// Libera recursos
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                _currentOperationTokenSource?.Cancel();
                _currentOperationTokenSource?.Dispose();
                _currentOperationTokenSource = null;
                IsOperationInProgress = false;
                CurrentOperationName = null;
            }
        }
    }

    /// <summary>
    /// Extensiones para facilitar el uso de CancellationToken
    /// </summary>
    public static class CancellationExtensions
    {
        /// <summary>
        /// Verifica cancelación cada N iteraciones en un bucle
        /// </summary>
        /// <param name="token">Token de cancelación</param>
        /// <param name="currentIteration">Iteración actual</param>
        /// <param name="checkInterval">Intervalo de verificación</param>
        public static void ThrowIfCancelledEvery(this CancellationToken token, int currentIteration, int checkInterval = 100)
        {
            if (currentIteration % checkInterval == 0)
            {
                token.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Delay cancelable
        /// </summary>
        /// <param name="token">Token de cancelación</param>
        /// <param name="milliseconds">Milisegundos a esperar</param>
        public static async Task DelayAsync(this CancellationToken token, int milliseconds)
        {
            try
            {
                await Task.Delay(milliseconds, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Re-lanzar con el token correcto
                throw new OperationCanceledException("Operación cancelada durante delay", token);
            }
        }
    }
}