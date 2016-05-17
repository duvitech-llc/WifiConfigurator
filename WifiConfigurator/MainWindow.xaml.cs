using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WifiConfigurator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private void LogText(string text)
        {
            Console.WriteLine(text);
        }

        public MainWindow()
        {
            InitializeComponent();

            LogText("Staring application");

            this.tbSSID.TabIndex = 0;
            this.tbPassPhrase.TabIndex = 1;
            this.tbType.TabIndex = 2;
            this.tbType.Text = "WPA-PSK";
            this.tbType.IsEnabled = false;

            this.tbSSID.Focus();
            LogText("Ready");
        }

        private Process createSilentProcess(string cmd, string args)
        {
            Process process = new Process();


            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;


            // Setup executable and parameters
            process.StartInfo.FileName = cmd;
            process.StartInfo.Arguments = args;

            return process;
        }

        private void btnConfig_Click(object sender, RoutedEventArgs e)
        {

            if (String.IsNullOrEmpty(tbSSID.Text) || String.IsNullOrEmpty(tbPassPhrase.Text))
            {
                MessageBox.Show("Must provide SSID and Passphrase");
                this.tbSSID.Focus();
                return;
            }

            LogText("Configuring HUD");

            var process = createSilentProcess(@".\platform-tools\adb", "start");

            process.Start();
            process.WaitForExit();

            LogText("Turning off WIFI");
            process = createSilentProcess(@".\platform-tools\adb", "shell svc wifi disable");

            process.Start();
            process.WaitForExit();

            LogText("Copy Template");
            File.Copy(@".\platform-tools\template\wpa_supplicant.conf", @".\wpa_supplicant.conf");

            LogText("Adding Configuration");
            string text = File.ReadAllText(@".\wpa_supplicant.conf");
            text = text.Replace("{SSID}", tbSSID.Text.Trim());
            text = text.Replace("{PASSPHRASE}", tbPassPhrase.Text.Trim());
            File.WriteAllText(@".\wpa_supplicant.conf", text);

            LogText("Pushing Configuration");
            process = createSilentProcess(@".\platform-tools\adb", @"push wpa_supplicant.conf /data/misc/wifi/wpa_supplicant.conf");

            process.Start();
            process.WaitForExit();

            LogText("Delete file");
            File.Delete(@".\wpa_supplicant.conf");

            LogText("Changing ownership of file");
            process = createSilentProcess(@".\platform-tools\adb", @"shell chown system.wifi /data/misc/wifi/wpa_supplicant.conf");

            process.Start();
            process.WaitForExit();


            LogText("Changing access rights of file");
            process = createSilentProcess(@".\platform-tools\adb", @"shell chmod 660 /data/misc/wifi/wpa_supplicant.conf");

            process.Start();
            process.WaitForExit();

            LogText("Turning on WIFI");
            process = createSilentProcess(@".\platform-tools\adb", "shell svc wifi enable");

            process.Start();
            process.WaitForExit();


            LogText("Finished");
            MessageBox.Show("Configuration Finished");
        }
    }
}
