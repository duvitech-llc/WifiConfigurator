using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ComponentModel; // CancelEventArgs
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
using System.Windows.Threading;
using System.Threading;

namespace WifiConfigurator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<string> androidDevices = new List<string>();
        private int logcount = 0;
        private bool bConnected = false;
        private bool bScanInProgress = false;
        private bool bSURequired = false;
        private bool bCommandExecuting = false;
        private string response;
        private Process scanProcess = null;
        private Process commandProcess = null;
        StreamWriter myWriter = null;

        private void LogText(string text)
        {
            logcount++;
            Debug.WriteLine(text);

            if (logcount > 3)
            {
                logcount = 1;
                Application.Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => this.tbLog.Text = text + "\r\n"));
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => this.tbLog.Text += text + "\r\n"));
            }
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

            this.tbSSID.Text = "vigeltek";
            this.tbPassPhrase.Text = "v1geltek";

            this.tbSSID.Focus();

            this.btnConnect.Content = "Open";
            bConnected = false;

            LogText("Ready");
            this.cbDeviceList.ItemsSource = androidDevices;
            scanAndroidDevices();
        }

        private Process createSilentProcess(string cmd, string args)
        {
            Process process = new Process();
            
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;


            // Setup executable and parameters
            process.StartInfo.FileName = cmd;
            if(!String.IsNullOrEmpty(args))
                process.StartInfo.Arguments = args;

            return process;
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {

            if (bConnected)
            {
                // send command exit
                sendCommandString("exit");
            }

            var process = createSilentProcess(@".\platform-tools\adb", "kill-server");

            process.Start();

            process.WaitForExit();
            
            LogText("Exited application");
        }

        private void scanAndroidDevices()
        {
            if (bScanInProgress)
            {
                MessageBox.Show("Scan currently in progress", "Busy");
                return;
            }

            bScanInProgress = true;

            LogText("Scanning for android devices");

            androidDevices.Clear();

            scanProcess = createSilentProcess(@".\platform-tools\adb", "devices");

            scanProcess.OutputDataReceived += Scan_OutputDataReceived;
            scanProcess.Exited += new EventHandler(Scan_Exited);
            scanProcess.EnableRaisingEvents = true;
            scanProcess.Start();
            scanProcess.BeginOutputReadLine();
        }

        private void Scan_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                LogText(e.Data);
                if (e.Data.EndsWith("device"))
                {
                    string sn = e.Data.Split(new char[] { '\t' })[0];
                    androidDevices.Add(sn);
                }
            }
        }

        private void Scan_Exited(object sender, EventArgs e)
        {
            bScanInProgress = false;
            if (androidDevices.Count > 0)
            {
                Application.Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => this.cbDeviceList.SelectedItem = androidDevices[0]));
            }else
            {
                MessageBox.Show("No attached devices", "Device not found");
            }
            
            scanProcess.Close();
            scanProcess = null;
        }

        private void btnConfig_Click(object sender, RoutedEventArgs e)
        {
            bool bFailed = false;
            if (!bConnected)
            {
                MessageBox.Show("Must connect to an android device first", "Not Connected");
                return;
            }

            if (String.IsNullOrEmpty(tbSSID.Text) || String.IsNullOrEmpty(tbPassPhrase.Text))
            {
                MessageBox.Show("Must provide SSID and Passphrase", "Invalid Entries");
                this.tbSSID.Focus();
                return;
            }
            tbLog.Text = "";
            LogText("Configuring HUD");
            // building string entry
            StringBuilder sbNetwork = new StringBuilder();
            sbNetwork.AppendLine("network={");
            sbNetwork.AppendLine("\tssid=\""+ tbSSID.Text.Trim() + "\"");
            sbNetwork.AppendLine("\tpsk=\"" + tbPassPhrase.Text.Trim() + "\"");
            sbNetwork.AppendLine("\tkey_mgmt=WPA-PSK");
            sbNetwork.AppendLine("\tpriority=10");
            sbNetwork.AppendLine("}");

            if (bSURequired)
            {
                LogText("Issuing SU Command to get root");
                if (sendCommandString("su").CompareTo("su") != 0)
                {
                    //error
                    MessageBox.Show("Error issuing su command", "Command Error");
                    return;
                }
            }

            LogText("Removing config file");
            if (sendCommandString("rm /data/misc/wifi/wpa_supplicant.conf").CompareTo("rm /data/misc/wifi/wpa_supplicant.conf") != 0)
            {
                LogText("Error removing old config");
                bFailed = true;

            }

            Thread.Sleep(1000);

            LogText("Turning off WIFI");
            if(sendCommandString("svc wifi disable").CompareTo("svc wifi disable") == 0)
            {
                Thread.Sleep(5000);

                sendCommandString("echo '" + sbNetwork.ToString() + "' >> /data/misc/wifi/wpa_supplicant.conf");

                
                if (sendCommandString(@"chown system.wifi /data/misc/wifi/wpa_supplicant.conf").CompareTo(@"chown system.wifi /data/misc/wifi/wpa_supplicant.conf") != 0)
                {
                    LogText("Error issuing chown command");
                    bFailed = true;
                }

                if (sendCommandString(@"chmod 660 /data/misc/wifi/wpa_supplicant.conf").CompareTo(@"chmod 660 /data/misc/wifi/wpa_supplicant.conf") != 0)
                {
                    LogText("Error issuing chmod command");
                    bFailed = true;
                }

                Thread.Sleep(1000);
                LogText("Turning on WIFI");
                sendCommandString("svc wifi enable");
                if (sendCommandString("svc wifi enable").CompareTo("svc wifi enable") != 0)
                {
                    LogText("Error issuing Enable Wifi Command");
                    bFailed = true;
                }
            }
            else
            {
                //error
                //error
                MessageBox.Show("Error issuing su command", "Command Error");
                return;
            }

            /*

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
            
            */


            if (bSURequired)
            {
                LogText("exiting root");
                sendCommandString("exit");
            }

            LogText("Finished");
            if (bFailed)
            {
                MessageBox.Show("Configuration Failed", "Error");
            }
            else
            {
                MessageBox.Show("Configuration Completed Successfully", "Completed");
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            scanAndroidDevices();
        }

        private void Command_Exited(object sender, EventArgs e)
        {
            LogText("Command Processor Exited");
            bCommandExecuting = false;
            // disconnected
            Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => this.btnConnect.Content = "Open"));

            bConnected = false;
            if (myWriter != null)
                myWriter.Close();
            
            myWriter = null;
            if(commandProcess != null)
                commandProcess.Close();
            commandProcess = null;
        }

        private string sendCommandString(string cmd)
        {
            if (!bConnected)
            {
                MessageBox.Show("Must connect to an android device first", "Not Connected");
                return string.Empty;
            }
            if (bCommandExecuting)
            {
                LogText("busy wait your turn");
                return string.Empty;
            }

            if (bConnected && myWriter != null)
            {
                bCommandExecuting = true;
                response = string.Empty;
                // send command 
                myWriter.WriteLine(cmd);
                while (bCommandExecuting)
                {
                    Thread.Sleep(100);
                }

                return response;

            }
            else
            {
                MessageBox.Show("Error Sending Command to Android Device", "Error");
            }

            return string.Empty;
        }

        private void Command_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                LogText("REC: " + e.Data);
                int promptIndex = e.Data.IndexOf('#');
                if(promptIndex < 0)
                {
                    promptIndex = e.Data.IndexOf('$');
                    if (promptIndex >= 0)
                        bSURequired = true;
                }

                if (promptIndex < 0)
                {
                    LogText("Undetected: " + e.Data);
                    response = e.Data.Trim();
                }
                else
                {
                    response = e.Data.Substring(promptIndex + 1).Trim();
                    LogText("Response: " + response);
                }
            }

            bCommandExecuting = false;
        }

        private void Command_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                LogText("Error: " + e.Data);
            }
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (bConnected)
            {
                // send command exit
                sendCommandString("exit");

                // disconnect
                this.btnConnect.Content = "Open";
                bConnected = false;
            }else
            {
                // not connected
                if(this.cbDeviceList.SelectedItem == null)
                {
                    MessageBox.Show("Must select device to connect to", "No Selected Device");
                    return;
                }
                bSURequired = false;
                LogText("Device Selected: " + this.cbDeviceList.SelectedItem);
                commandProcess = createSilentProcess(@".\platform-tools\adb" , "-s " + this.cbDeviceList.SelectedItem.ToString().Trim() + " shell");

                commandProcess.Exited += new EventHandler(Command_Exited);
                commandProcess.OutputDataReceived += Command_OutputDataReceived;
                commandProcess.ErrorDataReceived += Command_ErrorDataReceived;
                commandProcess.EnableRaisingEvents = true;                
                commandProcess.Start();
                myWriter = commandProcess.StandardInput;
                commandProcess.BeginErrorReadLine();
                commandProcess.BeginOutputReadLine();
                bConnected = true;
                myWriter.WriteLine();
                this.btnConnect.Content = "Close";
            }
        }
    }
}
