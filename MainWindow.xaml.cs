using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PerformanceMeasurement;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;
    private PerformanceCounter? _diskCounter;
    private DispatcherTimer? _timer;
    
    private string _lastDisplayText = "";
    private readonly StringBuilder _stringBuilder = new();
    private bool _isDragging;

    private readonly GpuMonitor _gpuMonitor = new();

    public MainWindow()
    {
        InitializeComponent();
        InitializePerformanceCounters();
        SetupWindow();
        StartMonitoring();
    }

    private void InitializePerformanceCounters()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            _gpuMonitor.Initialize();
            
            _cpuCounter.NextValue();
            _ramCounter.NextValue(); 
            _diskCounter.NextValue();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing performance counters: {ex.Message}");
        }
    }

    private void SetupWindow()
    {
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        
        // Check if installer configured startup
        HandleInstallerStartupSetting();
    }

    private void HandleInstallerStartupSetting()
    {
        try
        {
            // Check if installer set a startup preference
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\PerformanceMonitor", false);
            var startupEnabled = key?.GetValue("StartupEnabled");
            
            if (startupEnabled != null && bool.Parse(startupEnabled.ToString()!))
            {
                // Installer requested startup - enable it
                EnableStartup();
                
                // Clear the installer flag so we don't re-enable if user disables later
                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\PerformanceMonitor");
                writeKey.DeleteValue("StartupEnabled", false);
            }
        }
        catch
        {
            // Silently fail
        }
    }

    private void EnableStartup()
    {
        try
        {
            string executablePath = Environment.ProcessPath ?? AppContext.BaseDirectory + "PerformanceMeasurement.exe";
            
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("PerformanceMonitor", executablePath);
        }
        catch
        {
            // Silently fail
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = true;
            _timer?.Stop();
            
            try
            {
                DragMove();
            }
            finally
            {
                _isDragging = false;
                _timer?.Start();
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _timer?.Start();
        }
    }

    private void StartMonitoring()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += UpdateStats;
        _timer.Start();
    }

    private void UpdateStats(object? sender, EventArgs e)
    {
        if (_isDragging)
            return;

        try
        {
            float cpuUsage = _cpuCounter?.NextValue() ?? 0f;
            float ramUsage = _ramCounter?.NextValue() ?? 0f;
            float diskUsage = _diskCounter?.NextValue() ?? 0f;
            float gpuUsage = _gpuMonitor.GetGpuUsage();

            _stringBuilder.Clear();
            _stringBuilder.Append("CPU: ").Append(cpuUsage.ToString("F1")).Append("% | ");
            _stringBuilder.Append("RAM: ").Append(ramUsage.ToString("F1")).Append("% | ");
            _stringBuilder.Append("GPU: ").Append(gpuUsage.ToString("F1")).Append("% | ");
            _stringBuilder.Append("Disk: ").Append(diskUsage.ToString("F1")).Append("%");
            
            string newText = _stringBuilder.ToString();
            
            if (newText != _lastDisplayText)
            {
                StatsTextBlock.Text = newText;
                _lastDisplayText = newText;
            }
        }
        catch (Exception)
        {
            if (StatsTextBlock.Text != "Error reading performance data")
            {
                StatsTextBlock.Text = "Error reading performance data";
                _lastDisplayText = "Error reading performance data";
            }
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _diskCounter?.Dispose();
        
        _gpuMonitor.Dispose();
        
        GC.SuppressFinalize(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}