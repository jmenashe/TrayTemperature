using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Text;
using System.Reflection;

using OpenHardwareMonitor.Hardware;

namespace TrayTemperature {
	static class Program {
		static Dictionary<HardwareType, Queue<float>> TemperatureReadings = new Dictionary<HardwareType, Queue<float>>();
		static bool isLogging = false;
		static readonly string TempUnit = Properties.Settings.Default.IsFahrenheit ? "°F" : "°C";

		static Computer computer = new Computer()
		{ 
			CPUEnabled = true, 
			//GPUEnabled = true, 
			//FanControllerEnabled = true,
			HDDEnabled = true,
			//MainboardEnabled = true,
			RAMEnabled = true
		};
		static Timer tmr;
		static NotifyIcon ni;
		static ContextMenu contextMenu;
		static StreamWriter sw;

		[STAThread]
		static void Main() {
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			Properties.Settings.Default.Upgrade();

			//Inititalize OpenHardwareMonitorLib
			computer.Open();

			//Setup timer
			tmr = new Timer {
				Interval = Properties.Settings.Default.Refresh * 1000,
				Enabled = true
			};

			tmr.Tick += tmr_tick;

			//Setup context menu
			contextMenu = new ContextMenu();
			contextMenu.MenuItems.AddRange(new MenuItem[] {
				new MenuItem {
					Name = "menCel",
					Text = "Celsius",
					Checked = true,
				},
				new MenuItem {
					Name = "menFah",
					Text = "Fahrenheit"
				},
				new MenuItem {
					Text = "-",
				},
				new MenuItem {
					Name = "menRefresh",
					Text = "Refresh",
				},
				new MenuItem {
					Name = "menLog",
					Text = "Log"
				},
				new MenuItem {
					Name = "menReset",
					Text = "Reset statistics"
				},
				new MenuItem {
					Text = "-",
				},
				new MenuItem {
					Name = "menExit",
					Text = "Exit"
				},
			});

			//Refresh rate context sub-menus
			MenuItem refreshMenu = contextMenu.MenuItems.Find("menRefresh", false).First();
			refreshMenu.MenuItems.AddRange(new MenuItem[] {
				new MenuItem { Name = "1", Text = "1s" },
				new MenuItem { Name = "2", Text = "2s"},
				new MenuItem { Name = "5", Text = "5s" },
				new MenuItem { Name = "10", Text = "10s" },
				new MenuItem { Name = "15", Text = "15s" },
				new MenuItem { Name = "30", Text = "30s" },
				new MenuItem { Name = "60", Text = "60s" }
			});

			//Check the correct refresh rate MenuItem based on saved settings
			refreshMenu.MenuItems.Find(Properties.Settings.Default.Refresh.ToString(), false).First().Checked = true;

			//Add event listeners to the menus
			foreach (MenuItem menuItem in contextMenu.MenuItems)
				menuItem.Click += menu_Click;

			foreach (MenuItem menuItem in refreshMenu.MenuItems)
				menuItem.Click += menuRefresh_Click; ;

			//Check either Celsius or Fahrenheit based on saved settings
			if (Properties.Settings.Default.IsFahrenheit) {
				contextMenu.MenuItems.Find("menCel", false).First().Checked = true;
				contextMenu.MenuItems.Find("menFah", false).First().Checked = false;
			} else {
				contextMenu.MenuItems.Find("menCel", false).First().Checked = false;
				contextMenu.MenuItems.Find("menFah", false).First().Checked = true;
			}

			//Setup tray icon
			ni = new NotifyIcon {
				Visible = true,
				ContextMenu = contextMenu
			};

			//Enforce first tick as soon as possible
			tmr_tick(null, null);

			Application.Run();

			//Save settings when exiting
			Properties.Settings.Default.Save();

			ni.Visible = false;
		}

		private static void menuRefresh_Click(object sender, EventArgs e) {
			MenuItem clicked = (MenuItem)sender;

			//Uncheck all other intervals
			foreach (MenuItem item in clicked.Parent.MenuItems)
				item.Checked = false;

			//Check current
			clicked.Checked = true;

			//Update program settings
			Properties.Settings.Default.Refresh = Convert.ToInt32(clicked.Name);
			tmr.Interval = Properties.Settings.Default.Refresh * 1000;
			
		}

		//Handles context menu items click
		private static void menu_Click(object sender, EventArgs e) {
			switch (((MenuItem)sender).Name) {
				case "menCel":
					Properties.Settings.Default.IsFahrenheit = true;

					contextMenu.MenuItems.Find("menCel", false).First().Checked = true;
					contextMenu.MenuItems.Find("menFah", false).First().Checked = false;
					break;
				case "menFah":
					Properties.Settings.Default.IsFahrenheit = false;

					contextMenu.MenuItems.Find("menCel", false).First().Checked = false;
					contextMenu.MenuItems.Find("menFah", false).First().Checked = true;
					break;
				case "menExit":
					Application.Exit();
					break;
				case "menReset":
					TemperatureReadings.Clear();
					break;
				case "menLog":
					if (!isLogging) {
						if (MessageBox.Show("Starting a log will reset the current average, minimum and maximum temperatures. Proceed?", "Log start", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
							return;

						//Create a temp file to register temperatures at each timestamp. This will be concatenated with the statistics file when the log ends
						sw = new StreamWriter(string.Format("temp.log", DateTime.Now), false);
						sw.WriteLine("DateTime,CPU Temperature,GPU Temperature");

						TemperatureReadings.Clear();

						isLogging = true;

						//Disable unit change while logging
						contextMenu.MenuItems.Find("menCel", false).First().Enabled = false;
						contextMenu.MenuItems.Find("menFah", false).First().Enabled = false;
						contextMenu.MenuItems.Find("menLog", false).First().Checked = true;
					} else {
						sw.Close();
						sw = null;

						//Create the summary table
						StringBuilder sb = new StringBuilder();
						sb.AppendLine("Hardware,Average,Minimum,Maximum");
						foreach (var key in TemperatureReadings.Keys)
						{
							var queue = TemperatureReadings[key];
							sb.AppendLine($"{key},{queue.Average():F2},{queue.Min():F2},{queue.Max():F2}");
						}
						sb.AppendLine("");

						//Append the summary table with the temp timeseries and remove the temp log
						string fileName = string.Format("{0:yyyy-MM-dd_hh-mm-ss}.log", DateTime.Now);
						File.WriteAllText(fileName, sb.ToString() + File.ReadAllText("temp.log"));
						File.Delete("temp.log");

						MessageBox.Show("Log saved to:\r\n\r\n" + Path.Combine(Application.ExecutablePath, fileName), "Log saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

						isLogging = false;

						//Enable unit when logging ends
						contextMenu.MenuItems.Find("menCel", false).First().Enabled = true;
						contextMenu.MenuItems.Find("menFah", false).First().Enabled = true;
						contextMenu.MenuItems.Find("menLog", false).First().Checked = false;
					}
					break;
			}

			//Ensure prompt update after the user selects something from the context menu
			tmr_tick(null, null);
		}

		//Updates the temperatures
		private static void tmr_tick(object sender, EventArgs e) {

			Dictionary<HardwareType, Color> hardwareColors = new Dictionary<HardwareType, Color>();
			Dictionary<HardwareType, float> readings = new Dictionary<HardwareType, float>();
			List<(string, Color)> iconDatapoints = new List<(string, Color)>();
			//Updates the sensors on each hardware part
			foreach (IHardware hardware in computer.Hardware)
			{
				hardware.Update();

				// Read (converted) temperature
				if (!TemperatureReadings.TryGetValue(hardware.HardwareType, out Queue<float> queue))
					queue = TemperatureReadings[hardware.HardwareType] = new Queue<float>();
				ISensor sensor = hardware.Sensors.FirstOrDefault(d => d.SensorType == SensorType.Temperature);
				if (sensor?.Value == null)
					continue;
				float temp;
				if (Properties.Settings.Default.IsFahrenheit)
					temp = sensor.Value.Value * 1.8f + 32.0f;
				else
					temp = sensor.Value.Value;

				// Update the temp records
				queue.Enqueue(temp);
				while (queue.Count > Properties.Settings.Default.TemperatureHistoryLength)
					queue.Dequeue();
				readings[sensor.Hardware.HardwareType] = temp;

				// Determine temp color
				if (temp >= Properties.Settings.Default.HighTemp)
					hardwareColors[sensor.Hardware.HardwareType] = ColorTranslator.FromHtml(Properties.Settings.Default.HighColor);
				else if (temp >= Properties.Settings.Default.MediumTemp)
					hardwareColors[sensor.Hardware.HardwareType] = ColorTranslator.FromHtml(Properties.Settings.Default.MediumColor);
				else
					hardwareColors[sensor.Hardware.HardwareType] = ColorTranslator.FromHtml(Properties.Settings.Default.LowColor);

				var iconText = $"{readings[sensor.Hardware.HardwareType]:F0}{TempUnit}";
				var iconColor = hardwareColors[sensor.Hardware.HardwareType];
				iconDatapoints.Add((iconText, iconColor));
			}

			//Appends a new line to the current log file (CSV format)
			if (isLogging && sw != null)
            {
				List<string> csvFields = new List<string> { DateTime.Now.ToString() };
				foreach(var key in TemperatureReadings.Keys)
                {
					var lastValue = TemperatureReadings[key].Last();
					csvFields.Add(lastValue.ToString("0.00"));
                }
				sw.WriteLine(string.Join(",", csvFields));

			}

			//Updates the tooltip with the little hacky function
			StringBuilder sb = new StringBuilder();
			foreach(var key in readings.Keys)
            {
				var queue = TemperatureReadings[key];
				sb.AppendLine(key.ToString());
				sb.AppendLine($" {queue.Average():F1}{TempUnit} ~ [{queue.Min():F0},{queue.Max():F0}]");
            }

			SetNotifyIconText(ni, sb.ToString());
			ni.Icon = DynamicIcon.CreateIcon(iconDatapoints);
		}

		//Little hack to bypass the 63 char limit of the WinForms tooltip (still limited to the 127 chars of regular Win32 control)
		public static void SetNotifyIconText(NotifyIcon ni, string text) {
			if (text.Length >= 128)
				throw new ArgumentOutOfRangeException("Text limited to 127 characters");

			Type t = typeof(NotifyIcon);
			BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
			t.GetField("text", hidden).SetValue(ni, text);

			if ((bool)t.GetField("added", hidden).GetValue(ni))
				t.GetMethod("UpdateIcon", hidden).Invoke(ni, new object[] { true });
		}
	}
}
