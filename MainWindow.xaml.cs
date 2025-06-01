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
    private readonly List<PerformanceCounter> _gpuCounters = new();
    private DispatcherTimer? _timer;
    
    private string _lastDisplayText = "";
    private readonly StringBuilder _stringBuilder = new();
    private bool _isDragging;

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
            InitializeGpuCounters();
            
            _cpuCounter.NextValue();
            _ramCounter.NextValue(); 
            _diskCounter.NextValue();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing performance counters: {ex.Message}");
        }
    }

    private void InitializeGpuCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();
            
            foreach (var instanceName in instanceNames)
            {
                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName);
                    _gpuCounters.Add(counter);
                    counter.NextValue();
                }
                catch (Exception)
                {
                    // Skip counters that can't be initialized
                }
            }
        }
        catch (Exception)
        {
            // GPU Engine counters not available
        }
    }

    private void SetupWindow()
    {
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        
        // Auto-enable startup on the first run
        EnableStartupIfNotSet();
    }

    private void EnableStartupIfNotSet()
    {
        try
        {
            if (!IsStartupEnabled())
            {
                string executablePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue("PerformanceMonitor", executablePath);
            }
        }
        catch
        {
            // Silently fail if we can't set startup - it's not critical
        }
    }

    private bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("PerformanceMonitor") != null;
        }
        catch
        {
            return false;
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
            float gpuUsage = GetGpuUsage();

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

    private float GetGpuUsage()
    {
        if (_gpuCounters.Count == 0)
            return 0.0f;

        try
        {
            float totalUsage = 0f;
            int activeEngines = 0;
            float maxUsage = 0f;
            
            foreach (var t in _gpuCounters)
            {
                float usage = t.NextValue();
                
                if (usage > 0.1f) // Only count engines that are actually active
                {
                    totalUsage += usage;
                    activeEngines++;
                }
                
                if (usage > maxUsage)
                    maxUsage = usage;
            }
            
            if (activeEngines > 0)
            {
                float avgUsage = totalUsage / activeEngines;
                return MathF.Max(avgUsage, maxUsage * 0.8f);
            }
            
            return maxUsage;
        }
        catch (Exception)
        {
            return 0.0f;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _diskCounter?.Dispose();
        
        foreach (var counter in _gpuCounters)
        {
            counter.Dispose();
        }
        _gpuCounters.Clear();
        
        GC.SuppressFinalize(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}