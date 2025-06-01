using System.Diagnostics;

namespace PerformanceMeasurement;

/// <summary>
/// GPU monitoring utility that tries vendor-specific tools before falling back to Performance Counters
/// </summary>
public class GpuMonitor : IDisposable
{
    private readonly List<PerformanceCounter> _perfCounters = new();
    private bool _useNvidiaSmi;
    private bool _useAmdRocmSmi;

    public void Initialize()
    {
        // Try vendor-specific tools in order of preference
        DetectNvidiaSmi();
        
        if (!_useNvidiaSmi)
        {
            DetectAmdTools();
        }
        
        // If no vendor tools are available, fall back to Performance Counters
        if (!_useNvidiaSmi && !_useAmdRocmSmi)
        {
            InitializePerformanceCounters();
        }
    }
    
    private void DetectNvidiaSmi()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                _useNvidiaSmi = true;
            }
        }
        catch
        {
            _useNvidiaSmi = false;
        }
    }
    
    private void DetectAmdTools()
    {
        DetectAmdRocmSmi();
    }
    
    private void DetectAmdRocmSmi()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "rocm-smi",
                    Arguments = "--showuse --csv",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) && output.Contains("GPU use (%)"))
            {
                _useAmdRocmSmi = true;
            }
        }
        catch
        {
            _useAmdRocmSmi = false;
        }
    }
    

    
    private void InitializePerformanceCounters()
    {
        try
        {
            // Try multiple counter categories for GPU monitoring
            var gpuCategories = new[] { "GPU Engine", "GPU Process Memory", "GPU Adapter Memory" };
            
            foreach (var categoryName in gpuCategories)
            {
                try
                {
                    var category = new PerformanceCounterCategory(categoryName);
                    var instanceNames = category.GetInstanceNames();
                    
                    foreach (var instanceName in instanceNames)
                    {
                        // For GPU Engine look for utilization counters
                        if (categoryName == "GPU Engine")
                        {
                            try
                            {
                                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName);
                                _perfCounters.Add(counter);
                                counter.NextValue();
                            }
                            catch (Exception)
                            {
                                // Skip counters that can't be initialized
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // GPU category not available, skip
                }
            }
        }
        catch (Exception)
        {
            // GPU initialization failed
        }
    }
    
    public float GetGpuUsage()
    {
        if (_useNvidiaSmi)
        {
            return GetNvidiaGpuUsage();
        }
        else if (_useAmdRocmSmi)
        {
            return GetAmdRocmSmiGpuUsage();
        }

        else
        {
            return GetPerformanceCounterGpuUsage();
        }
    }
    
    private float GetNvidiaGpuUsage()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && float.TryParse(output, out float usage))
            {
                return usage;
            }
        }
        catch
        {
            // If nvidia-smi fails, fall back to performance counters for future calls
            _useNvidiaSmi = false;
            InitializePerformanceCounters();
        }
        
        return 0.0f;
    }
    
    private float GetAmdRocmSmiGpuUsage()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "rocm-smi",
                    Arguments = "--showuse --csv",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("GPU use (%)") && !line.StartsWith("GPU"))
                    {
                        var parts = line.Split(',');
                        if (parts.Length > 1 && float.TryParse(parts[1].Trim().Replace("%", ""), out float usage))
                        {
                            return usage;
                        }
                    }
                }
            }
        }
        catch
        {
            // If rocm-smi fails, fall back to performance counters
            _useAmdRocmSmi = false;
            InitializePerformanceCounters();
        }
        
        return 0.0f;
    }
    

    
    private float GetPerformanceCounterGpuUsage()
    {
        if (_perfCounters.Count == 0)
            return 0.0f;

        try
        {
            float totalUsage = 0f;
            int activeEngines = 0;
            float maxUsage = 0f;
            
            foreach (var counter in _perfCounters)
            {
                float usage = counter.NextValue();
                
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
        foreach (var counter in _perfCounters)
        {
            counter.Dispose();
        }
        _perfCounters.Clear();
        
        GC.SuppressFinalize(this);
    }
} 