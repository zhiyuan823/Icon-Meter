﻿using Microsoft.Win32;
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

namespace IconMeterWPF
{
	class MainViewModel : INotifyPropertyChanged
	{
		// private fields
		private PerformanceMeter _meter;
		private PopupPerformanceMeter _popupMeter;

		// properties
		public Window MainWindow { get; set; }
		public PerformanceMeter Meter { get => _meter; private set => SetField(ref _meter, value); }
		public PopupPerformanceMeter PopupMeter { get => _popupMeter; private set => SetField(ref _popupMeter, value); }
		public ICommand StartTaskManager { get; private set; }
		public ICommand ShowPopup { get; private set; }

		// constructors
		public MainViewModel()
		{
			// initial all public ICommand objects
			InitCommands();

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
			// start Task Manager
			Process p = new Process();
			p.StartInfo.FileName = "taskmgr";
			p.Start();
		}

		private void _ShowPopup(object obj = null)
		{
			// show popup window
			var w = this.MainWindow as MainWindow;
			w?.ShowPopup();
		}

		void UpdateAutoStartSetting()
		{
            
			// The path to the key where Windows looks for startup applications
			RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

			if (Properties.Settings.Default.RunAtStartup)
			{
				// Add the value in the registry so that the application runs at startup
				rkApp.SetValue("IconMeter", System.Reflection.Assembly.GetExecutingAssembly().Location);
			}
			else
			{
				// Remove the value from the registry so that the application doesn't start
				rkApp.DeleteValue("IconMeter", false);
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
