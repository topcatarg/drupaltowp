using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace drupaltowp.ViewModels;

public class LoggerViewModel : INotifyPropertyChanged
{
    private readonly StringBuilder _sb = new();
    private readonly object _lock = new();
    private const int MAX_LOG_SIZE = 100000;


    private string _logText;
    public string LogText
    {
        get => _logText;
        private set
        {
            if (_logText != value)
            {
                _logText = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _autoScroll = true;
    public bool AutoScroll
    {
        get => _autoScroll;
        set
        {
            if (_autoScroll != value)
            {
                _autoScroll = value;
                OnPropertyChanged();
            }
        }
    }

    public LoggerViewModel()
    {
        LogInfo("Sistema de migracion iniciado");
    }

    public void LogMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] {message}";

        lock (_lock)
        {
            _sb.AppendLine(entry);
            if (_sb.Length > MAX_LOG_SIZE)
            {
                TrimLog();
            }

            LogText = _sb.ToString();

        }
    }

    public void TrimLog()
    {
        var text = _sb.ToString();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var keepLines = lines.Length / 2;

        _sb.Clear();
        for (int i = keepLines; i < lines.Length; i++)
        {
            if (!string.IsNullOrEmpty(lines[i]))
            {
                _sb.AppendLine(lines[i]);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _sb.Clear();
            LogText = "";
        }
    }

    public void LogSuccess(string message) => LogMessage($"✅ {message}");
    public void LogError(string message) => LogMessage($"❌ {message}");

    public void LogWarning(string message) => LogMessage($"⚠️ {message}");
    public void LogInfo(string message) => LogMessage($"ℹ️ {message}");
    public void LogProcess(string message) => LogMessage($"🔁 {message}");
    public void LogCompleted(string message) => LogMessage($"🎉 {message}");

    public void LogProgress(string operation, int current, int total, int batchSize = 100)
    {
        if (current % batchSize == 0 || current == total)
        {
            var percentage = (current * 100 / total);
            LogInfo($"📊 {operation}: {current:N0}/{total:N0} ({percentage:F1}%)");
        }
    }

    public void LogBatch(string message, int count)
    {
        LogInfo($"📦 {message}: {count:N0} elementos procesados");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
