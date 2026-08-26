using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using log4net;

namespace IconMeterWPF
{
	class MainViewModel : INotifyPropertyChanged
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(MainViewModel));

		// private fields
		private PerformanceMeter _meter;
		private PopupPerformanceMeter _popupMeter;

		// the name of the scheduled task used for auto start at logon.
		// it is created under the "IconMeter" folder in Task Scheduler for better organization.
		private const string AutoStartTaskName = @"IconMeter\IconMeter";

		// properties
		public Window MainWindow { get; set; }
		public PerformanceMeter Meter { get => _meter; private set => SetField(ref _meter, value); }
		public PopupPerformanceMeter PopupMeter { get => _popupMeter; private set => SetField(ref _popupMeter, value); }
		public ICommand StartTaskManager { get; private set; }
		public ICommand ShowPopup { get; private set; }

		/// <summary>
		/// Gets a value indicating whether the current process is running with administrator privileges.
		/// CPU/GPU temperature readings require elevation, so the related settings are disabled otherwise.
		/// </summary>
		public bool IsAdministrator { get; } = App.IsAdministrator();

		// constructors
		public MainViewModel()
		{
			log.Info("MainViewModel constructor called");
			
			// initial all public ICommand objects
			InitCommands();

			// remove the legacy registry run key so the app is not started twice at logon
			RemoveLegacyRunKey();

			// keep the RunAtStartup / RunAsAdmin checkboxes in sync with the actual scheduled task state
			bool taskExists = AutoStartTaskExists();
			Properties.Settings.Default.RunAtStartup = taskExists;
			Properties.Settings.Default.RunAsAdmin = taskExists && AutoStartTaskRunsAsAdmin();

			// create meters
			Meter = new PerformanceMeter();
			PopupMeter = new PopupPerformanceMeter(Meter);
		}

		// private methods
		private void InitCommands()
		{
			// init ICommand objects for binding
			StartTaskManager = new RelayCommand(_StartTaskManager);
			ShowPopup = new RelayCommand(_ShowPopup);
		}

		private void _StartTaskManager(object obj = null)
		{
			log.Info("Task Manager started via StartTaskManager command");
			
			// start Task Manager
			Process p = new Process();
			p.StartInfo.FileName = "taskmgr";
			p.Start();
		}

		private void _ShowPopup(object obj = null)
		{
			log.Info("Popup window shown via ShowPopup command");
			
			// show popup window
			var w = this.MainWindow as MainWindow;
			w?.ShowPopup();
		}

		void UpdateAutoStartSetting()
		{
			// remove the legacy registry run key to avoid double startup
			RemoveLegacyRunKey();

			if (Properties.Settings.Default.RunAtStartup)
			{
				if (Properties.Settings.Default.RunAsAdmin)
				{
					// registering a task with the highest privileges requires administrator
					// rights. If the app is already running elevated, create it directly
					// (no UAC prompt). Otherwise run the helper script elevated via "runas"
					// (a UAC prompt appears once, only when the user actively enables this
					// option). At logon the task scheduler then starts the app with admin
					// rights silently, without a UAC prompt.
					string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
					string tempScript = Path.Combine(Path.GetTempPath(), "IconMeterSetupTask.ps1");
					using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("IconMeterWPF.SetupTask.ps1"))
					using (FileStream fs = File.Create(tempScript))
					{
						stream.CopyTo(fs);
					}

					string args = string.Format("-Action create -TaskName \"{0}\" -Executable \"{1}\"", AutoStartTaskName, exePath);
					int exitCode = App.IsAdministrator()
						? RunSetupScript(tempScript, args, false)
						: RunSetupScript(tempScript, args, true);

					if (exitCode != 0)
					{
						log.Error("Failed to create the elevated auto start scheduled task (exit code " + exitCode + ")");
						// the user declined the UAC prompt or the operation failed: revert the checkbox
						Properties.Settings.Default.RunAsAdmin = false;
						MessageBox.Show(Properties.Resources.RunAsAdminFailed,
							Properties.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
					}
				}
				else
				{
					// a normal (non-elevated) task can be created directly from this process
					string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
					string arguments = string.Format("/create /tn \"{0}\" /tr \"\\\"{1}\\\"\" /sc onlogon /f", AutoStartTaskName, exePath);
					int exitCode = RunScheduledTaskCommand(arguments);
					if (exitCode != 0)
						log.Error("Failed to create the auto start scheduled task (exit code " + exitCode + ")");
				}
			}
			else
			{
				// remove the scheduled task so that the application doesn't start at logon
				int exitCode = RunScheduledTaskCommand(string.Format("/delete /tn \"{0}\" /f", AutoStartTaskName));
				if (exitCode != 0)
					log.Error("Failed to delete the auto start scheduled task (exit code " + exitCode + ")");
			}
		}

		/// <summary>
		/// Runs the helper PowerShell script. When <paramref name="elevate"/> is true the
		/// script is launched elevated via "runas" (triggering a UAC prompt) so it can
		/// register the highest-privilege scheduled task. Returns the script's exit code.
		/// </summary>
		int RunSetupScript(string scriptPath, string arguments, bool elevate)
		{
			try
			{
				Process p = new Process();
				p.StartInfo.FileName = "powershell.exe";
				p.StartInfo.Arguments = string.Format("-NoProfile -ExecutionPolicy Bypass -File \"{0}\" {1}", scriptPath, arguments);
				p.StartInfo.UseShellExecute = true;
				p.StartInfo.CreateNoWindow = true;
				if (elevate)
					p.StartInfo.Verb = "runas";
				p.Start();
				p.WaitForExit();
				return p.ExitCode;
			}
			catch (System.ComponentModel.Win32Exception ex)
			{
				// ERROR_CANCELLED (1223): the user declined the UAC prompt
				log.Error("The elevated setup was cancelled or failed to start", ex);
				return ex.NativeErrorCode == 1223 ? 1223 : -1;
			}
			catch (Exception ex)
			{
				log.Error("Failed to run the elevated setup script", ex);
				return -1;
			}
		}

		/// <summary>
		/// Checks whether the auto start scheduled task exists.
		/// </summary>
		bool AutoStartTaskExists()
		{
			return RunScheduledTaskCommand(string.Format("/query /tn \"{0}\"", AutoStartTaskName)) == 0;
		}

		/// <summary>
		/// Checks whether the auto start scheduled task runs with the highest privileges.
		/// </summary>
		bool AutoStartTaskRunsAsAdmin()
		{
			string output = RunScheduledTaskCommandWithOutput(string.Format("/query /tn \"{0}\" /xml", AutoStartTaskName));
			return output != null && output.IndexOf("<RunLevel>HighestAvailable</RunLevel>", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Removes the legacy registry run key used by previous versions,
		/// so the app is not started twice at logon.
		/// </summary>
		void RemoveLegacyRunKey()
		{
			try
			{
				using (RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
				{
					rkApp?.DeleteValue("IconMeter", false);
				}
			}
			catch (Exception ex)
			{
				log.Error("Failed to remove the legacy registry run key", ex);
			}
		}

		/// <summary>
		/// Runs a schtasks.exe command and returns its exit code.
		/// </summary>
		int RunScheduledTaskCommand(string arguments)
		{
			try
			{
				Process p = new Process();
				p.StartInfo.FileName = "schtasks.exe";
				p.StartInfo.Arguments = arguments;
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.CreateNoWindow = true;
				p.Start();
				p.WaitForExit();
				return p.ExitCode;
			}
			catch (Exception ex)
			{
				log.Error("Failed to run schtasks.exe", ex);
				return -1;
			}
		}

		/// <summary>
		/// Runs a schtasks.exe command and returns its standard output (or null on failure).
		/// </summary>
		string RunScheduledTaskCommandWithOutput(string arguments)
		{
			try
			{
				Process p = new Process();
				p.StartInfo.FileName = "schtasks.exe";
				p.StartInfo.Arguments = arguments;
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.CreateNoWindow = true;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.RedirectStandardError = true;
				p.Start();
				string output = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				return output;
			}
			catch (Exception ex)
			{
				log.Error("Failed to run schtasks.exe", ex);
				return null;
			}
		}

		// public methods
		public void ReloadSettings()
        {
            Properties.Settings.Default.Reload();
        }
		public void SaveSettings()
        {
			// restart application if languate setting is updated
			if (Properties.Settings.Default.Language != System.Threading.Thread.CurrentThread.CurrentUICulture.Name)
			{
				// set the is restarting flag
				Properties.Settings.Default.IsRestarting = true;
				Properties.Settings.Default.Save();

				// start a new instance of application
				Process.Start(Application.ResourceAssembly.Location);

				// close the current instance of application
				Application.Current.Shutdown();
			}
			// otherwise save setting and reset meter
			else
			{
				// save settings
				Properties.Settings.Default.Save();

				// reset meters
				Meter.ResetPerformanceMeter();

				// save auto start setting too
				UpdateAutoStartSetting();
			}
		}
        public void PauseUpdate()
        {
			Meter.Pause();
        }
		public void ResumeUpdate()
        {
			Meter.Resume();
        }

		// INotifyPropertyChanged implementation
		public event PropertyChangedEventHandler PropertyChanged;
		protected void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
		{
			if (EqualityComparer<T>.Default.Equals(field, value)) return;
			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
