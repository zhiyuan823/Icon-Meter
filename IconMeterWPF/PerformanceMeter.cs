﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Management;
using System.Reflection;
using LibreHardwareMonitor.Hardware;

namespace IconMeterWPF
{
	sealed class PerformanceMeter : IDisposable, INotifyPropertyChanged
	{
		[DllImport("user32.dll")]
		extern static bool DestroyIcon(IntPtr handle);

		// private fields
		Properties.Settings settings;
		PerformanceCounter cpuCounter, memoryCounter, diskCounter;

		/// <summary>
		/// The last cpu usage
		/// </summary>
		float lastCpuUsage = 0;

        /// <summary>
        /// The computer hardware
        /// </summary>
        Computer computer = new Computer() { IsCpuEnabled = true , IsGpuEnabled = true };

        /// <summary>
        /// The cpu
        /// </summary>
        IHardware cpu;

        /// <summary>
        /// Gets or sets the cpu temperature sensor.
        /// </summary>
        /// <value>
        /// The cpu temperature sensor.
        /// </value>
        public ISensor CpuTemperatureSensor { get; set; }

        /// <summary>
        /// The gpu
        /// </summary>
        IHardware gpu;

        /// <summary>
        /// Gets or sets the gpu load sensor.
        /// </summary>
        /// <value>
        /// The gpu load sensor.
        /// </value>
        public ISensor GpuLoadSensor { get; set; }

        /// <summary>
        /// Gets or sets the gpu temperature sensor.
        /// </summary>
        /// <value>
        /// The gpu temperature sensor.
        /// </value>
        public ISensor GpuTemperatureSensor { get; set; }

        /// <summary>
        /// Gets or sets the cpu temperature sensors.
        /// </summary>
        /// <value>
        /// The cpu temperature sensors.
        /// </value>
        public List<String> CpuTemperatureSensors { get; set;}

        /// <summary>
        /// Gets or sets the gpu temperature sensors.
        /// </summary>
        /// <value>
        /// The gpu temperature sensors.
        /// </value>
        public List<String> GpuTemperatureSensors { get; set;}

		float lastMemoryUsage = 0;
		float lastDiskUsage = 0;
		float _lastNetworkReceive = 0;
		float _lastNetworkSend = 0;
		float[] logicalProcessorUsage = null;
		IEnumerable<(float usage, int index)> selectedLogicalProcessorUsage = null;
		string _mainTooltip, _logicalProcessorsTooltip;
		Icon _defaultTrayIcon, _mainTrayIcon, _logicalProcessorsTrayIcon;
		IEnumerable<float> _lastNetworkSpeed;

		// private readonly fields
		readonly DispatcherTimer timer = new DispatcherTimer();
		readonly float totalMemorySize;
		readonly List<PerformanceCounter> networkReceiveCounters = new List<PerformanceCounter>();
		readonly List<PerformanceCounter> networkSendCounters = new List<PerformanceCounter>();
		readonly List<PerformanceCounter> logicalProcessorsCounter = new List<PerformanceCounter>();
		readonly Queue<float> previousNetwordReceive = new Queue<float>();
		readonly Queue<float> previousNetwordSend = new Queue<float>();
		const string upArrow = "\u25b2";
		const string downArrow = "\u25bc";

		// public propertie
		public Icon DefaultTrayIcon {
			get => _defaultTrayIcon;
			set => SetField(ref _defaultTrayIcon, value);
		}
		public Icon MainTrayIcon {
			get => _mainTrayIcon;
			set => SetField(ref _mainTrayIcon, value);
		}
		public string MainTooltip {
			get => _mainTooltip;
			set => SetField(ref _mainTooltip, value);
		}
		public Icon LogicalProcessorsTrayIcon {
			get => _logicalProcessorsTrayIcon;
			set => SetField(ref _logicalProcessorsTrayIcon, value);
		}
		public string LogicalProcessorsTooltip {
			get => _logicalProcessorsTooltip;
			set => SetField(ref _logicalProcessorsTooltip, value);
		}
		public float LastNetworkReceive {
			get => _lastNetworkReceive;
			set => SetField(ref _lastNetworkReceive, value);
		}
		public float LastNetworkSend {
			get => _lastNetworkSend;
			set => SetField(ref _lastNetworkSend, value);
		}
		public IEnumerable<float> LastNetworkSpeed {
			get => _lastNetworkSpeed;
			set => SetField(ref _lastNetworkSpeed, value);
		}

		// constructor
		public PerformanceMeter()
		{
			totalMemorySize = (float)GetTotalMemorySize();

			// initialize queues and timer
			previousNetwordReceive.Enqueue(0);
			previousNetwordSend.Enqueue(0);
			timer.Tick += new EventHandler(Timer_Tick);
			timer.Interval = new TimeSpan(0, 0, 1);

			// initialize all performance counters
			ResetPerformanceMeter();
		}

		// public method
		public void ResetPerformanceMeter()
		{
			// dispose all current performance counters
			DisposePerformanceCounters();

            // update the setting field
            //this.settings = settings;
            this.settings = Properties.Settings.Default;

            // create all performance counters with new settings
            InitializePerformanceCounters();
		}

		public void Pause()
		{
			this.timer.Stop();
		}

		public void Resume()
		{
			this.timer.Start();
		}

		// event handler
		private void Timer_Tick(object sender, EventArgs e)
		{
			// first get new readings from performance counters
			UpdateReadings();

			// update icon image and tooltip of main tray icon
			var oldIcon = MainTrayIcon;
			MainTrayIcon = BuildMainNotifyIcon();
			MainTooltip = BuildMainTooltip();

			// dispose the original icon to ensure resources are released
			if (oldIcon != null && oldIcon != DefaultTrayIcon)
			{
				DestroyIcon(oldIcon.Handle);
			}

			// if logical processor usage is displaying 
			if (settings.ShowLogicalProcessorsUsage)
			{
				// update icon image and tooltip of logical processor tray icon
				oldIcon = LogicalProcessorsTrayIcon;
				LogicalProcessorsTrayIcon = BuildLogicalProcessorIcon();
				LogicalProcessorsTooltip = BuildLogicalProcessorTooltip();

				// dispose the original icon to ensure resources are released
				if (oldIcon != null && oldIcon != DefaultTrayIcon)
				{
					DestroyIcon(oldIcon.Handle);
				}
			}
		}

		// private methods
		void InitializePerformanceCounters()
		{
			// initialize necessary performance counters depending on current setting

			// Processor PC
			// in virtual machine the "Processor Information" category may not found,
			// therefore use "Processor" category if exception occurs
			try
			{
				cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
			}
			catch (Exception)
			{
				cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
			}

			// Initial CPU
			computer.Open();
            cpu = computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu).FirstOrDefault();
            if(cpu != null)
			{
				CpuTemperatureSensors = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature).Select(s => s.Name).ToList();

                CpuTemperatureSensor = cpu.Sensors
                .Where(s => s.SensorType == SensorType.Temperature && s.Value != null && s.Name == settings.CpuTemperatureKey).FirstOrDefault();
            }

			// Initial GPU
			gpu = computer.Hardware.Where(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel).FirstOrDefault();
			if (gpu != null)
			{
                GpuTemperatureSensors = gpu.Sensors.Where(s => s.SensorType == SensorType.Temperature).Select(s => s.Name).ToList();

                GpuLoadSensor = gpu.Sensors.Where(s => s.SensorType == SensorType.Load).FirstOrDefault();
                GpuTemperatureSensor = gpu.Sensors
                    .Where(s => s.SensorType == SensorType.Temperature && s.Value != null && s.Name == settings.GpuTemperatureKey).FirstOrDefault();
            }			

            // memory PC
            memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
			//"Memory", "% Committed Bytes In Use");

			diskCounter = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total");

			// network PC
			PerformanceCounterCategory networkCounterCategory
				= new PerformanceCounterCategory("Network Interface");
			foreach (string name in networkCounterCategory.GetInstanceNames())
			{
				networkReceiveCounters.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", name));
				networkSendCounters.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", name));
			}

			// logical processor PCs
			var processorCategory = new PerformanceCounterCategory("Processor Information");
			var logicalProcessorNames = processorCategory.GetInstanceNames()
				.Where(s => !s.Contains("Total"))
				.OrderBy(s => s);
			int nLogicalProcessors = logicalProcessorNames.Count();
			foreach (string name in logicalProcessorNames)
			{
				try
				{
					logicalProcessorsCounter.Add(new PerformanceCounter("Processor Information", "% Processor Utility", name));
				}
				catch
				{
					logicalProcessorsCounter.Add(new PerformanceCounter("Processor Information", "% Processor Time", name));
				}
			}
			this.logicalProcessorUsage = new float[logicalProcessorsCounter.Count];

			timer.Start();
		}
		void DisposePerformanceCounters()
		{
			// dispose all performance counters and clean up

			// first stop timer
			timer.Stop();

			// dispose all performance counters and reset fields
			cpuCounter?.Dispose();
			memoryCounter?.Dispose();
			diskCounter?.Dispose();
			cpuCounter = null;
			memoryCounter = null;
			diskCounter = null;

			foreach (var pc in networkReceiveCounters) pc?.Dispose();
			foreach (var pc in networkSendCounters) pc?.Dispose();
			foreach (var pc in logicalProcessorsCounter) pc?.Dispose();
			networkReceiveCounters.Clear();
			networkSendCounters.Clear();
			logicalProcessorsCounter.Clear();
		}
		void UpdateReadings()
		{
			// get the next readings from performance counters

			// CPU, memory and disk
			if (settings.ShowCpuUsage) lastCpuUsage = cpuCounter.NextValue();
			if (settings.ShowMemoryUsage) lastMemoryUsage = 100 - (memoryCounter.NextValue() / totalMemorySize) * 100;
			if (settings.ShowDiskUsage) lastDiskUsage = 100 - diskCounter.NextValue();

			// update cpu data
			cpu.Update();

			// update gpu data
			gpu.Update();

			// network 
			//if (settings.ShowNetworkUsage)
			{
				float lastNetworkReceive = 0;
				foreach (var c in networkReceiveCounters)
					lastNetworkReceive += c.NextValue();

				previousNetwordReceive.Enqueue(lastNetworkReceive);
				if (previousNetwordReceive.Count > 60)
					previousNetwordReceive.Dequeue();

				float  lastNetworkSend = 0;
				foreach (var c in networkSendCounters)
					lastNetworkSend += c.NextValue();

				previousNetwordSend.Enqueue(lastNetworkSend);
				if (previousNetwordSend.Count > 60)
					previousNetwordSend.Dequeue();

				LastNetworkReceive = lastNetworkReceive;
				LastNetworkSend = lastNetworkSend;
				LastNetworkSpeed = new float[] { lastNetworkReceive, lastNetworkSend };
			}

			// logical processors
			if (settings.ShowLogicalProcessorsUsage)
			{
				for (int i = 0; i < logicalProcessorsCounter.Count; i++)
					logicalProcessorUsage[i] = logicalProcessorsCounter[i].NextValue();


				if (settings.ShowOnlyTheMostUtilizedProcessors &&
					settings.NumberOfShownProcessors < logicalProcessorUsage.Count())
				{
					int n = settings.NumberOfShownProcessors;
					selectedLogicalProcessorUsage = logicalProcessorUsage
						.Select((usage, index) => (usage, index))
						.OrderByDescending(t => t.usage)
						.Take(n);
				}
			}
		}
		Icon BuildMainNotifyIcon()
		{
			// prepare list of display readings and brushes
			List<(float, Brush)> list = new List<(float, Brush)>();

			if (settings.ShowCpuUsage)
				list.Add((lastCpuUsage, new SolidBrush(settings.CpuColor)));

			if (settings.ShowMemoryUsage)
				list.Add((lastMemoryUsage, new SolidBrush(settings.MemoryColor)));

			if (settings.ShowDiskUsage)
				list.Add((lastDiskUsage, new SolidBrush(settings.DiskColor)));

			if (settings.ShowNetworkUsage)
			{
				// compute the moving maximum network flow value
				float maxNetworkReceive = previousNetwordReceive.Max();
				float maxNetworkSend = previousNetwordSend.Max();
				float maxNetword = Math.Max(maxNetworkReceive, maxNetworkSend);

				// compute relative flow
				float send = (_lastNetworkSend / maxNetword) * 100;
				float receive = (_lastNetworkReceive / maxNetword) * 100;

				list.Add((send, new SolidBrush(settings.NetworkReceiveColor)));
				list.Add((receive, new SolidBrush(settings.NetworkSendColor)));
			}

			// return default icon if no items is selected
			if (list.Count == 0)
			{
				return DefaultTrayIcon;
			}

			// build the new icon
			Icon icon = IconBuilder.BuildIcon(list, useVerticalBar: settings.UseVerticalBars);
		
			// release resource used by brushes
			foreach (var (_, brush) in list) brush.Dispose();

			// return the icon
			return icon;
		}
		Icon BuildLogicalProcessorIcon()
		{
			// create brush for drawing
			Color color = settings.LogicalProcessorColor;
			Brush brush = new SolidBrush(color);

			IEnumerable<float> usages = logicalProcessorUsage;

			// order and filter processor usages if only the most utilized processors will be shown
			if (settings.ShowOnlyTheMostUtilizedProcessors && 
				settings.NumberOfShownProcessors < usages.Count())
			{
				usages = selectedLogicalProcessorUsage.Select(x => x.usage);
			}

			// build the new icon from logical processor readings
			Icon icon = IconBuilder.BuildIcon(
				usages.Select(x => (x, brush)),
				useVerticalBar: settings.UseVerticalBars,
				label: "P"
				);

			// release resource used by brushes
			brush.Dispose();

			// return the icon
			return icon;
		}
		string BuildMainTooltip()
		{
			// build notify icon's tooltip text

			// detemine unit for network flow readings
			float nr = _lastNetworkReceive / 1024;
			float ns = _lastNetworkSend / 1024;
			string unit = "KBps";
			if (nr > 1024 || ns > 1024)
			{
				nr = nr / 1024;
				ns = ns / 1024;
				unit = "MBps";
			}

			// build the text
			StringBuilder sb = new StringBuilder();

			if (settings.ShowCpuUsage) sb.AppendLine($"{Properties.Resources.CPU} {Math.Round(lastCpuUsage)}%");
			if (settings.ShowCpuTemperature && CpuTemperatureSensor != null) sb.AppendLine($"{Properties.Resources.CPUTemperature} {Math.Round(CpuTemperatureSensor.Value ?? 0)} ({Math.Round(CpuTemperatureSensor.Min ?? 0)}, {Math.Round(CpuTemperatureSensor.Max ?? 0)})");

			if (settings.ShowGpuUsage && GpuLoadSensor != null) sb.AppendLine($"{Properties.Resources.GPU} {Math.Round(GpuLoadSensor.Value ?? 0)}%");
            if (settings.ShowGpuTemperature && GpuTemperatureSensor != null) sb.AppendLine($"{Properties.Resources.GPUTemperature} {Math.Round(GpuTemperatureSensor.Value ?? 0)} ({Math.Round(GpuTemperatureSensor.Min ?? 0)}, {Math.Round(GpuTemperatureSensor.Max ?? 0)})");

            if (settings.ShowMemoryUsage) sb.AppendLine($"{Properties.Resources.Memory} {Math.Round(lastMemoryUsage)}%");
			if (settings.ShowDiskUsage) sb.AppendLine($"{Properties.Resources.Disk} {Math.Round(lastDiskUsage)}%");
			if (settings.ShowNetworkUsage)
			{
				sb.Append($"{Properties.Resources.Network} {downArrow}:" + nr.ToString("0.0"));
				sb.Append($" {upArrow}:" + ns.ToString("0.0") + " " + unit);
			}

			// show product name and version if no meter is selected
			if (sb.Length == 0)
			{
				object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
				if (attributes.Length >= 0)
				{
					sb.AppendLine(((AssemblyProductAttribute)attributes[0]).Product);
				}
				string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
				sb.Append(String.Format("{0} {1}", Properties.Resources.Version, version));
			}

			// make sure the tooltip text has at most 128 characters
			if (sb.Length >= 128) sb.Remove(127, sb.Length - 127);

			// return the text value
			return sb.ToString().TrimEnd();
		}
		string BuildLogicalProcessorTooltip()
		{
			// build notify icon's tooltip text for logical processors

			IEnumerable<(float usage, int index)> usages = logicalProcessorUsage.Select((usage, index) => (usage, index));


			if (settings.ShowOnlyTheMostUtilizedProcessors &&
				settings.NumberOfShownProcessors < logicalProcessorUsage.Count())
			{
				usages = selectedLogicalProcessorUsage;
			}

			// build the text
			StringBuilder sb = new StringBuilder();
			foreach ((float usage, int index) in usages)
			{
				_ = sb.AppendLine($"{Properties.Resources.CPU} {index + 1}: {Math.Round(usage)}%");
			}

			// make sure the tooltip text has at most 128 characters
			if (sb.Length >= 128) sb.Remove(127, sb.Length - 127);

			// return the text value
			return sb.ToString().TrimEnd();
		}
		double GetTotalMemorySize()
		{
			ObjectQuery wql = new ObjectQuery("SELECT * FROM Win32_OperatingSystem");
			ManagementObjectSearcher searcher = new ManagementObjectSearcher(wql);
			ManagementObjectCollection results = searcher.Get();

			foreach (ManagementObject result in results)
			{
				double res = Convert.ToDouble(result["TotalVisibleMemorySize"]);
				return res / 1024;
			}
			return 0;
		}

		// IDisposable implementation
		public void Dispose()
		{
			DisposePerformanceCounters();
		}

		// INotifyPropertyChanged implementation
		public event PropertyChangedEventHandler PropertyChanged;
		void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(field, value)) return;
			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public static class ConvertExtesions
	{
		public static System.Drawing.Color ToGDIColor(this System.Windows.Media.Color c)
		{
			return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
		}

	}


}
