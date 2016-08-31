﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using NiceHashMiner.Configs;
using NiceHashMiner.Devices;
using NiceHashMiner.Enums;
using NiceHashMiner.Forms;
using NiceHashMiner.Miners;
using NiceHashMiner.Interfaces;
using NiceHashMiner.Forms.Components;
using NiceHashMiner.Utils;

namespace NiceHashMiner
{
    public partial class Form_Main : Form, Form_Loading.IAfterInitializationCaller, IMainFormRatesComunication
    {
        private static string VisitURL = "http://www.nicehash.com";

        private Timer MinerStatsCheck;
        private Timer UpdateCheck;
        private Timer SMACheck;
        private Timer BalanceCheck;
        private Timer SMAMinerCheck;
        private Timer BitcoinExchangeCheck;
        private Timer StartupTimer;
        private Timer IdleCheck;

        private bool ShowWarningNiceHashData;
        private bool DemoMode;

        private Random R;

        private Form_Loading _downloadUnzipForm;
        private Form_Loading LoadingScreen;
        private Form BenchmarkForm;

        int flowLayoutPanelVisibleCount = 0;
        int flowLayoutPanelRatesIndex = 0;

        
        const string _betaAlphaPostfixString = " BETA";

        private bool _isDeviceDetectionInitialized = false;

        public Form_Main()
        {
            InitializeComponent();

            InitLocalization();

            // Log the computer's amount of Total RAM and Page File Size
            ManagementObjectCollection moc = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_OperatingSystem").Get();
            foreach (ManagementObject mo in moc)
            {
                long TotalRam = long.Parse(mo["TotalVisibleMemorySize"].ToString()) / 1024;
                long PageFileSize = (long.Parse(mo["TotalVirtualMemorySize"].ToString()) / 1024) - TotalRam;
                Helpers.ConsolePrint("NICEHASH", "Total RAM: "      + TotalRam     + "MB");
                Helpers.ConsolePrint("NICEHASH", "Page File Size: " + PageFileSize + "MB");
            }

            R = new Random((int)DateTime.Now.Ticks);

            Text += " v" + Application.ProductVersion + _betaAlphaPostfixString;

            InitMainConfigGUIData();
        }

        private void InitLocalization() {
            MessageBoxManager.Unregister();
            MessageBoxManager.Yes = International.GetText("Global_Yes");
            MessageBoxManager.No = International.GetText("Global_No");
            MessageBoxManager.OK = International.GetText("Global_OK");
            MessageBoxManager.Register();

            labelServiceLocation.Text = International.GetText("Service_Location") + ":";
            labelBitcoinAddress.Text = International.GetText("BitcoinAddress") + ":";
            labelWorkerName.Text = International.GetText("WorkerName") + ":";

            linkLabelVisitUs.Text = International.GetText("form1_visit_us");
            linkLabelCheckStats.Text = International.GetText("form1_check_stats");
            linkLabelChooseBTCWallet.Text = International.GetText("form1_choose_bitcoin_wallet");

            // these strings are no longer used, check and use them as base
            string rateString = International.GetText("Rate") + ":";
            string ratesBTCInitialString = "0.00000000 BTC/" + International.GetText("Day");
            string ratesDollarInitialString = String.Format("0.00 {0}/", ConfigManager.Instance.GeneralConfig.DisplayCurrency) + International.GetText("Day");

            toolStripStatusLabelGlobalRateText.Text = International.GetText("form1_global_rate") + ":";
            toolStripStatusLabelBTCDayText.Text = "BTC/" + International.GetText("Day");
            toolStripStatusLabelBalanceText.Text = (ConfigManager.Instance.GeneralConfig.DisplayCurrency + "/") + International.GetText("Day") + "     " + International.GetText("form1_balance") + ":";

            devicesListViewEnableControl1.InitLocale();

            buttonBenchmark.Text = International.GetText("form1_benchmark");
            buttonSettings.Text = International.GetText("form1_settings");
            buttonStartMining.Text = International.GetText("form1_start");
            buttonStopMining.Text = International.GetText("form1_stop");
        }

        private void InitMainConfigGUIData() {
            if (ConfigManager.Instance.GeneralConfig.ServiceLocation >= 0 && ConfigManager.Instance.GeneralConfig.ServiceLocation < Globals.MiningLocation.Length)
                comboBoxLocation.SelectedIndex = ConfigManager.Instance.GeneralConfig.ServiceLocation;
            else
                comboBoxLocation.SelectedIndex = 0;

            textBoxBTCAddress.Text = ConfigManager.Instance.GeneralConfig.BitcoinAddress;
            textBoxWorkerName.Text = ConfigManager.Instance.GeneralConfig.WorkerName;
            ShowWarningNiceHashData = true;
            DemoMode = false;

            toolStripStatusLabelBalanceDollarValue.Text = "(" + ConfigManager.Instance.GeneralConfig.DisplayCurrency + ")";

            if (_isDeviceDetectionInitialized) {
                devicesListViewEnableControl1.ResetComputeDevices(ComputeDevice.AllAvaliableDevices);
            }
        }

        public void AfterLoadComplete()
        {
            // TODO dispose object, check LoadingScreen 
            LoadingScreen = null;

            this.Enabled = true;

            IdleCheck = new Timer();
            IdleCheck.Tick += IdleCheck_Tick;
            IdleCheck.Interval = 500;
            IdleCheck.Start();
        }


        private void IdleCheck_Tick(object sender, EventArgs e)
        {
            if (!ConfigManager.Instance.GeneralConfig.StartMiningWhenIdle) return;

            uint MSIdle = Helpers.GetIdleTime();

            if (MinerStatsCheck.Enabled)
            {
                if (MSIdle < (ConfigManager.Instance.GeneralConfig.MinIdleSeconds * 1000))
                {
                    buttonStopMining_Click(null, null);
                    Helpers.ConsolePrint("NICEHASH", "Resumed from idling");
                }
            }
            else
            {
                if (BenchmarkForm == null && (MSIdle > (ConfigManager.Instance.GeneralConfig.MinIdleSeconds * 1000)))
                {
                    Helpers.ConsolePrint("NICEHASH", "Entering idling state");
                    buttonStartMining_Click(null, null);
                }
            }
        }

        // This is a single shot _benchmarkTimer
        private void StartupTimer_Tick(object sender, EventArgs e)
        {
            StartupTimer.Stop();
            StartupTimer = null;

            if (!Helpers.InternalCheckIsWow64()) {
                MessageBox.Show("NiceHash Miner supports only x64 platforms. You will not be able to use NiceHash Miner with x86",
                                International.GetText("Warning_with_Exclamation"),
                                MessageBoxButtons.OK);

                this.Close();
                return;
            }

            // Query Avaliable ComputeDevices
            ComputeDeviceQueryManager.Instance.QueryDevices(LoadingScreen);
            _isDeviceDetectionInitialized = true;

            /////////////////////////////////////////////
            /////// from here on we have our devices and Miners initialized
            ConfigManager.Instance.AfterDeviceQueryInitialization();
            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_SaveConfig"));
            
            // All devices settup should be initialized in AllDevices
            devicesListViewEnableControl1.ResetComputeDevices(ComputeDevice.AllAvaliableDevices);
            // set properties after
            devicesListViewEnableControl1.AutoSaveChange = true;
            devicesListViewEnableControl1.SaveToGeneralConfig = true;

            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_CheckLatestVersion"));

            MinerStatsCheck = new Timer();
            MinerStatsCheck.Tick += MinerStatsCheck_Tick;
            MinerStatsCheck.Interval = ConfigManager.Instance.GeneralConfig.MinerAPIQueryInterval * 1000;

            SMAMinerCheck = new Timer();
            SMAMinerCheck.Tick += SMAMinerCheck_Tick;
            SMAMinerCheck.Interval = ConfigManager.Instance.GeneralConfig.SwitchMinSecondsFixed * 1000 + R.Next(ConfigManager.Instance.GeneralConfig.SwitchMinSecondsDynamic * 1000);
            if (ComputeDeviceGroupManager.Instance.GetGroupCount(DeviceGroupType.AMD_OpenCL) > 0) {
                SMAMinerCheck.Interval = (ConfigManager.Instance.GeneralConfig.SwitchMinSecondsAMD + ConfigManager.Instance.GeneralConfig.SwitchMinSecondsFixed) * 1000 + R.Next(ConfigManager.Instance.GeneralConfig.SwitchMinSecondsDynamic * 1000);
            }

            UpdateCheck = new Timer();
            UpdateCheck.Tick += UpdateCheck_Tick;
            UpdateCheck.Interval = 1000 * 3600; // every 1 hour
            UpdateCheck.Start();
            UpdateCheck_Tick(null, null);

            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_GetNiceHashSMA"));

            SMACheck = new Timer();
            SMACheck.Tick += SMACheck_Tick;
            SMACheck.Interval = 60 * 1000; // every 60 seconds
            SMACheck.Start();
            SMACheck_Tick(null, null);

            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_GetBTCRate"));

            BitcoinExchangeCheck = new Timer();
            BitcoinExchangeCheck.Tick += BitcoinExchangeCheck_Tick;
            BitcoinExchangeCheck.Interval = 1000 * 3601; // every 1 hour and 1 second
            BitcoinExchangeCheck.Start();
            BitcoinExchangeCheck_Tick(null, null);

            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_GetNiceHashBalance"));

            BalanceCheck = new Timer();
            BalanceCheck.Tick += BalanceCheck_Tick;
            BalanceCheck.Interval = 61 * 1000; // every 61 seconds
            BalanceCheck.Start();
            BalanceCheck_Tick(null, null);

            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_SetEnvironmentVariable"));

            SetEnvironmentVariables();

            LoadingScreen.IncreaseLoadCounterAndMessage(International.GetText("form1_loadtext_SetWindowsErrorReporting"));
            
            Helpers.DisableWindowsErrorReporting(ConfigManager.Instance.GeneralConfig.DisableWindowsErrorReporting);

            LoadingScreen.IncreaseLoadCounter();
            if (ConfigManager.Instance.GeneralConfig.NVIDIAP0State)
            {
                LoadingScreen.SetInfoMsg(International.GetText("form1_loadtext_NVIDIAP0State"));
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "nvidiasetp0state.exe";
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    psi.CreateNoWindow = true;
                    Process p = Process.Start(psi);
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                        Helpers.ConsolePrint("NICEHASH", "nvidiasetp0state returned error code: " + p.ExitCode.ToString());
                    else
                        Helpers.ConsolePrint("NICEHASH", "nvidiasetp0state all OK");
                }
                catch (Exception ex)
                {
                    Helpers.ConsolePrint("NICEHASH", "nvidiasetp0state error: " + ex.Message);
                }
            }

            LoadingScreen.FinishLoad();

            // check if download needed
            if (!MinersDownloadManager.Instance.IsMinersBinsInit()) {
                _downloadUnzipForm = new Form_Loading();
                _downloadUnzipForm.Location = new Point(this.Location.X + (this.Width - _downloadUnzipForm.Width) / 2, this.Location.Y + (this.Height - _downloadUnzipForm.Height) / 2);
                _downloadUnzipForm.ShowDialog();
            }
            // TODO no bots please
            if (ConfigManager.Instance.GeneralConfig.hwidLoadFromFile && !ConfigManager.Instance.GeneralConfig.hwidOK) {
                MessageBox.Show("IF YOU DID NOT INSTALL NICEHASH MINER ON THIS PC, YOUR PC MAY BE COMPROMISED!",
                                                            "Warning!",
                                                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void Form_Main_Shown(object sender, EventArgs e)
        {
            // general loading indicator
            // TODO fix loading steps
            int TotalLoadSteps = 12;
            LoadingScreen = new Form_Loading(this,
                International.GetText("form3_label_LoadingText"),
                International.GetText("form1_loadtext_CPU"), TotalLoadSteps);
            LoadingScreen.Location = new Point(this.Location.X + (this.Width - LoadingScreen.Width) / 2, this.Location.Y + (this.Height - LoadingScreen.Height) / 2);
            LoadingScreen.Show();

            StartupTimer = new Timer();
            StartupTimer.Tick += StartupTimer_Tick;
            StartupTimer.Interval = 200;
            StartupTimer.Start();
        }

        private void SMAMinerCheck_Tick(object sender, EventArgs e)
        {
            SMAMinerCheck.Interval = ConfigManager.Instance.GeneralConfig.SwitchMinSecondsFixed * 1000 + R.Next(ConfigManager.Instance.GeneralConfig.SwitchMinSecondsDynamic * 1000);
            if (ComputeDeviceGroupManager.Instance.GetGroupCount(DeviceGroupType.AMD_OpenCL) > 0) {
                SMAMinerCheck.Interval = (ConfigManager.Instance.GeneralConfig.SwitchMinSecondsAMD + ConfigManager.Instance.GeneralConfig.SwitchMinSecondsFixed) * 1000 + R.Next(ConfigManager.Instance.GeneralConfig.SwitchMinSecondsDynamic * 1000);
            }

#if (SWITCH_TESTING)
            SMAMinerCheck.Interval = MinersManager.SMAMinerCheckInterval;
#endif
            MinersManager.Instance.SwichMostProfitableGroupUpMethod(Globals.NiceHashData);
        }


        private void MinerStatsCheck_Tick(object sender, EventArgs e) {
            MinersManager.Instance.MinerStatsCheck(Globals.NiceHashData);
        }

        private void SetDeviceGroupStats(
            ref Label labelSpeed,
            ref Label labelRateBTC,
            ref Label labelRateCurrency,
            string aname, double speed, double paying)
        {
            labelSpeed.Text = Helpers.FormatSpeedOutput(speed) + aname;
            labelRateBTC.Text = FormatPayingOutput(paying);
            labelRateCurrency.Text = CurrencyConverter.CurrencyConverter.ConvertToActiveCurrency(paying * Globals.BitcoinRate).ToString("F2", CultureInfo.InvariantCulture)
                + String.Format(" {0}/", ConfigManager.Instance.GeneralConfig.DisplayCurrency) + International.GetText("Day");
            UpdateGlobalRate();
        }

        private void InitFlowPanelStart() {
            flowLayoutPanelRates.Controls.Clear();
            // add for every cdev a 
            foreach (var cdev in ComputeDevice.AllAvaliableDevices) {
                if(cdev.Enabled) {
                    var newGroupProfitControl = new GroupProfitControl();
                    newGroupProfitControl.Visible = false;
                    flowLayoutPanelRates.Controls.Add(newGroupProfitControl);
                }
            }
        }

        public void ClearRates(int groupCount) {
            if (flowLayoutPanelVisibleCount != groupCount) {
                flowLayoutPanelVisibleCount = groupCount;
                // hide some Controls
                int hideIndex = 0;
                foreach (var control in flowLayoutPanelRates.Controls) {
                    ((GroupProfitControl)control).Visible = hideIndex < groupCount ? true : false;
                    ++hideIndex;
                }
            }
            flowLayoutPanelRatesIndex = 0;
        }

        public void AddRateInfo(string groupName, string deviceStringInfo, APIData iAPIData, double paying, bool isApiGetException) {
            string ApiGetExceptionString = isApiGetException ? "**" : "";

            string speedString = Helpers.FormatSpeedOutput(iAPIData.Speed) + iAPIData.AlgorithmName + ApiGetExceptionString;
            string rateBTCString = FormatPayingOutput(paying);
            string rateCurrencyString = CurrencyConverter.CurrencyConverter.ConvertToActiveCurrency(paying * Globals.BitcoinRate).ToString("F2", CultureInfo.InvariantCulture)
                + String.Format(" {0}/", ConfigManager.Instance.GeneralConfig.DisplayCurrency) + International.GetText("Day");
            
            ((GroupProfitControl)flowLayoutPanelRates.Controls[flowLayoutPanelRatesIndex++])
                .UpdateProfitStats(groupName, deviceStringInfo, speedString, rateBTCString, rateCurrencyString);

            UpdateGlobalRate();
        }

        public void ShowNotProfitable() {
            label_MiningProfitabilityStatus.Text = "MINING PROTIFABILITY STATUS: MINING NOT PROFITABLE";
        }
        public void HideNotProfitable() {
            label_MiningProfitabilityStatus.Text = "MINING PROTIFABILITY STATUS: PROFITABLE";
        }

        private void UpdateGlobalRate()
        {
            double TotalRate = MinersManager.Instance.GetTotalRate();

            if (ConfigManager.Instance.GeneralConfig.AutoScaleBTCValues && TotalRate < 0.1)
            {
                toolStripStatusLabelBTCDayText.Text = "mBTC/" + International.GetText("Day");
                toolStripStatusLabelGlobalRateValue.Text = (TotalRate * 1000).ToString("F7", CultureInfo.InvariantCulture);
            }
            else
            {
                toolStripStatusLabelBTCDayText.Text = "BTC/" + International.GetText("Day");
                toolStripStatusLabelGlobalRateValue.Text = (TotalRate).ToString("F8", CultureInfo.InvariantCulture);
            }

            toolStripStatusLabelBTCDayValue.Text = CurrencyConverter.CurrencyConverter.ConvertToActiveCurrency((TotalRate * Globals.BitcoinRate)).ToString("F2", CultureInfo.InvariantCulture);
        }


        void BalanceCheck_Tick(object sender, EventArgs e)
        {
            if (VerifyMiningAddress(false))
            {
                Helpers.ConsolePrint("NICEHASH", "Balance get");
                double Balance = NiceHashStats.GetBalance(textBoxBTCAddress.Text.Trim(), textBoxBTCAddress.Text.Trim() + "." + textBoxWorkerName.Text.Trim());
                if (Balance > 0)
                {
                    if (ConfigManager.Instance.GeneralConfig.AutoScaleBTCValues && Balance < 0.1)
                    {
                        toolStripStatusLabelBalanceBTCCode.Text = "mBTC";
                        toolStripStatusLabelBalanceBTCValue.Text = (Balance * 1000).ToString("F7", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        toolStripStatusLabelBalanceBTCCode.Text = "BTC";
                        toolStripStatusLabelBalanceBTCValue.Text = Balance.ToString("F8", CultureInfo.InvariantCulture);
                    }

                    //Helpers.ConsolePrint("CurrencyConverter", "Using CurrencyConverter" + ConfigManager.Instance.GeneralConfig.DisplayCurrency);
                    double Amount = (Balance * Globals.BitcoinRate);
                    Amount = CurrencyConverter.CurrencyConverter.ConvertToActiveCurrency(Amount);
                    toolStripStatusLabelBalanceDollarText.Text = Amount.ToString("F2", CultureInfo.InvariantCulture);
                }
            }
        }


        void BitcoinExchangeCheck_Tick(object sender, EventArgs e)
        {
            Helpers.ConsolePrint("COINBASE", "Bitcoin rate get");
            double BR = Bitcoin.GetUSDExchangeRate();
            if (BR > 0) Globals.BitcoinRate = BR;
            Helpers.ConsolePrint("COINBASE", "Current Bitcoin rate: " + Globals.BitcoinRate.ToString("F2", CultureInfo.InvariantCulture));
        }


        void SMACheck_Tick(object sender, EventArgs e)
        {
            string worker = textBoxBTCAddress.Text.Trim() + "." + textBoxWorkerName.Text.Trim();
            Helpers.ConsolePrint("NICEHASH", "SMA get");
            Dictionary<AlgorithmType, NiceHashSMA> t = NiceHashStats.GetAlgorithmRates(worker);

            for (int i = 0; i < 3; i++)
            {
                if (t != null)
                {
                    Globals.NiceHashData = t;
                    break;
                }

                Helpers.ConsolePrint("NICEHASH", "SMA get failed .. retrying");
                System.Threading.Thread.Sleep(1000);
                t = NiceHashStats.GetAlgorithmRates(worker);
            }

            if (t == null && Globals.NiceHashData == null && ShowWarningNiceHashData)
            {
                ShowWarningNiceHashData = false;
                DialogResult dialogResult = MessageBox.Show(International.GetText("form1_msgbox_NoInternetMsg"),
                                                            International.GetText("form1_msgbox_NoInternetTitle"),
                                                            MessageBoxButtons.YesNo, MessageBoxIcon.Error);

                if (dialogResult == DialogResult.Yes)
                    return;
                else if (dialogResult == DialogResult.No)
                    System.Windows.Forms.Application.Exit();
            }
        }


        void UpdateCheck_Tick(object sender, EventArgs e)
        {
            Helpers.ConsolePrint("NICEHASH", "Version get");
            string ver = NiceHashStats.GetVersion(textBoxBTCAddress.Text.Trim() + "." + textBoxWorkerName.Text.Trim());

            if (ver == null) return;

            Version programVersion = new Version(Application.ProductVersion);
            Version onlineVersion = new Version(ver);
            int ret = programVersion.CompareTo(onlineVersion);

            if (ret < 0)
            {
                linkLabelVisitUs.Text = String.Format(International.GetText("form1_new_version_released"), ver);
                VisitURL = "https://github.com/nicehash/NiceHashMiner/releases/tag/" + ver;
            }
        }


        void SetEnvironmentVariables()
        {
            Helpers.ConsolePrint("NICEHASH", "Setting environment variables");

            string[] envName = { "GPU_MAX_ALLOC_PERCENT", "GPU_USE_SYNC_OBJECTS",
                                 "GPU_SINGLE_ALLOC_PERCENT", "GPU_MAX_HEAP_SIZE", "GPU_FORCE_64BIT_PTR" };
            string[] envValue = { "100", "1", "100", "100", "0" };

            for (int i = 0; i < envName.Length; i++)
            {
                // Check if all the variables is set
                if (Environment.GetEnvironmentVariable(envName[i]) == null)
                {
                    try { Environment.SetEnvironmentVariable(envName[i], envValue[i]); }
                    catch (Exception e) { Helpers.ConsolePrint("NICEHASH", e.ToString()); }
                }

                // Check to make sure all the values are set correctly
                if (!Environment.GetEnvironmentVariable(envName[i]).Equals(envValue[i]))
                {
                    try { Environment.SetEnvironmentVariable(envName[i], envValue[i]); }
                    catch (Exception e) { Helpers.ConsolePrint("NICEHASH", e.ToString()); }
                }
            }
        }


        private bool VerifyMiningAddress(bool ShowError)
        {
            if (!BitcoinAddress.ValidateBitcoinAddress(textBoxBTCAddress.Text.Trim()) && ShowError)
            {
                DialogResult result = MessageBox.Show(International.GetText("form1_msgbox_InvalidBTCAddressMsg"),
                                                      International.GetText("Error_with_Exclamation"),
                                                      MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                
                if (result == System.Windows.Forms.DialogResult.Yes)
                    System.Diagnostics.Process.Start("https://www.nicehash.com/index.jsp?p=faq#faqs15");
                
                textBoxBTCAddress.Focus();
                return false;
            }
            else if (!BitcoinAddress.ValidateWorkerName(textBoxWorkerName.Text.Trim()) && ShowError)
            {
                DialogResult result = MessageBox.Show(International.GetText("form1_msgbox_InvalidWorkerNameMsg"),
                                                      International.GetText("Error_with_Exclamation"),
                                                      MessageBoxButtons.OK, MessageBoxIcon.Error);

                textBoxWorkerName.Focus();
                return false;
            }

            return true;
        }


        private void linkLabelVisitUs_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(VisitURL);
        }


        private void linkLabelCheckStats_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (!VerifyMiningAddress(true)) return;

            System.Diagnostics.Process.Start("http://www.nicehash.com/index.jsp?p=miners&addr=" + textBoxBTCAddress.Text.Trim());
        }


        private void linkLabelChooseBTCWallet_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.nicehash.com/index.jsp?p=faq#faqs15");
        }


        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            MinersManager.Instance.StopAllMiners();

            MessageBoxManager.Unregister();
        }        

        private void buttonBenchmark_Click(object sender, EventArgs e)
        {
            ConfigManager.Instance.GeneralConfig.ServiceLocation = comboBoxLocation.SelectedIndex;

            SMACheck.Stop();
            BenchmarkForm = new FormBenchmark();
            BenchmarkForm.ShowDialog();
            BenchmarkForm = null;
            SMACheck.Start();
        }


        private void buttonSettings_Click(object sender, EventArgs e)
        {
            FormSettings Settings = new FormSettings();
            Settings.ShowDialog();

            if (Settings.IsChange && Settings.IsChangeSaved && Settings.IsRestartNeeded) {
                MessageBox.Show("Settings change requires NiceHash Miner to restart.",
                            "Info",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process PHandle = new Process();
                PHandle.StartInfo.FileName = Application.ExecutablePath;
                PHandle.Start();
                Close();
            } else if (Settings.IsChange && Settings.IsChangeSaved) {
                InitLocalization();
                InitMainConfigGUIData();
            }
        }


        private void buttonStartMining_Click(object sender, EventArgs e)
        {
            if (textBoxBTCAddress.Text.Equals(""))
            {
                DialogResult result = MessageBox.Show(International.GetText("form1_DemoModeMsg"),
                                                      International.GetText("form1_DemoModeTitle"),
                                                      MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    DemoMode = true;
                    labelDemoMode.Visible = true;
                    labelDemoMode.Text = International.GetText("form1_DemoModeLabel");

                    //textBoxBTCAddress.Text = "34HKWdzLxWBduUfJE9JxaFhoXnfC6gmePG";
                }
                else
                    return;
            }
            else if (!VerifyMiningAddress(true)) return;

            if (Globals.NiceHashData == null)
            {
                MessageBox.Show(International.GetText("form1_msgbox_NullNiceHashDataMsg"),
                                International.GetText("Error_with_Exclamation"),
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // first value is a boolean if initialized or not
            var tuplePair = DeviceBenchmarkConfigManager.Instance.IsEnabledBenchmarksInitialized();
            bool isBenchInit = tuplePair.Item1;
            Dictionary<string, List<AlgorithmType>> nonBenchmarkedPerDevice = tuplePair.Item2;
            // Check if the user has run benchmark first
            if (!isBenchInit) {
                // first benchmark and start mining
                string warningMsg = "Unbenchmarked enabled algorithms for enabled devices:" + Environment.NewLine; ;
                foreach (var kvp in nonBenchmarkedPerDevice) {
                    if (kvp.Value.Count != 0) {
                        warningMsg += kvp.Key + ": " + string.Join(", ", kvp.Value) + Environment.NewLine;
                    }
                }
                warningMsg += "Benchmark and start mining?";
                DialogResult result = MessageBox.Show(String.Format("{0}", warningMsg),
                                                          International.GetText("Warning_with_Exclamation"),
                                                          MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == System.Windows.Forms.DialogResult.Yes) {
                    SMACheck.Stop();
                    List<ComputeDevice> enabledDevices = new List<ComputeDevice>();
                    HashSet<string> deviceNames = new HashSet<string>();
                    foreach (var cdev in ComputeDevice.AllAvaliableDevices) {
                        if (cdev.Enabled && !deviceNames.Contains(cdev.Name)) {
                            deviceNames.Add(cdev.Name);
                            enabledDevices.Add(cdev);
                        }
                    }
                    BenchmarkForm = new FormBenchmark(
                        BenchmarkPerformanceType.Standard,
                        true,
                        enabledDevices);
                    BenchmarkForm.ShowDialog();
                    BenchmarkForm = null;
                    SMACheck.Start();
                } else {
                    return;
                }
            }

            textBoxBTCAddress.Enabled = false;
            textBoxWorkerName.Enabled = false;
            comboBoxLocation.Enabled = false;
            buttonBenchmark.Enabled = false;
            buttonStartMining.Enabled = false;
            buttonSettings.Enabled = false;
            devicesListViewEnableControl1.Enabled = false;
            buttonStopMining.Enabled = true;

            ConfigManager.Instance.GeneralConfig.BitcoinAddress = textBoxBTCAddress.Text.Trim();
            ConfigManager.Instance.GeneralConfig.WorkerName = textBoxWorkerName.Text.Trim();
            ConfigManager.Instance.GeneralConfig.ServiceLocation = comboBoxLocation.SelectedIndex;

            var btcAdress = DemoMode ? Globals.DemoUser : textBoxBTCAddress.Text.Trim();
            var isMining = MinersManager.Instance.StartInitialize(this, Globals.MiningLocation[comboBoxLocation.SelectedIndex], textBoxWorkerName.Text.Trim(), btcAdress);
            InitFlowPanelStart();

            if (!DemoMode) ConfigManager.Instance.GeneralConfig.Commit();

            SMAMinerCheck.Interval = 100;
            SMAMinerCheck.Start();
            //SMAMinerCheck_Tick(null, null);
            MinerStatsCheck.Start();
        }


        private void buttonStopMining_Click(object sender, EventArgs e)
        {
            MinerStatsCheck.Stop();
            SMAMinerCheck.Stop();

            MinersManager.Instance.StopAllMiners();

            textBoxBTCAddress.Enabled = true;
            textBoxWorkerName.Enabled = true;
            comboBoxLocation.Enabled = true;
            buttonBenchmark.Enabled = true;
            buttonStartMining.Enabled = true;
            buttonSettings.Enabled = true;
            devicesListViewEnableControl1.Enabled = true;
            buttonStopMining.Enabled = false;

            if (DemoMode)
            {
                DemoMode = false;
                labelDemoMode.Visible = false;

                //textBoxBTCAddress.Text = "";
                //ConfigManager.Instance.GeneralConfig.BitcoinAddress = "";
            }

            UpdateGlobalRate();
        }

        private string FormatPayingOutput(double paying)
        {
            string ret = "";

            if (ConfigManager.Instance.GeneralConfig.AutoScaleBTCValues && paying < 0.1)
                ret = (paying * 1000).ToString("F7", CultureInfo.InvariantCulture) + " mBTC/" + International.GetText("Day");
            else
                ret = paying.ToString("F8", CultureInfo.InvariantCulture) + " BTC/" + International.GetText("Day");

            return ret;
        }


        private void buttonHelp_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/nicehash/NiceHashMiner");
        }

        private void toolStripStatusLabel10_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.nicehash.com/index.jsp?p=faq#faqs6");
        }

        private void toolStripStatusLabel10_MouseHover(object sender, EventArgs e)
        {
            statusStrip1.Cursor = Cursors.Hand;
        }

        private void toolStripStatusLabel10_MouseLeave(object sender, EventArgs e)
        {
            statusStrip1.Cursor = Cursors.Default;
        }

        private void textBoxCheckBoxMain_Leave(object sender, EventArgs e)
        {
            if (VerifyMiningAddress(false))
            {
                // Commit to config.json
                ConfigManager.Instance.GeneralConfig.BitcoinAddress = textBoxBTCAddress.Text.Trim();
                ConfigManager.Instance.GeneralConfig.WorkerName = textBoxWorkerName.Text.Trim();
                ConfigManager.Instance.GeneralConfig.ServiceLocation = comboBoxLocation.SelectedIndex;
                ConfigManager.Instance.GeneralConfig.Commit();
            }
        }

        // Minimize to system tray if MinimizeToTray is set to true
        private void Form1_Resize(object sender, EventArgs e)
        {
            notifyIcon1.Icon = Properties.Resources.logo;
            notifyIcon1.Text = Application.ProductName + " v" + Application.ProductVersion + "\nDouble-click to restore..";

            if (ConfigManager.Instance.GeneralConfig.MinimizeToTray && FormWindowState.Minimized == this.WindowState)
            {
                notifyIcon1.Visible = true;
                this.Hide();
            }
        }

        // Restore NiceHashMiner from the system tray
        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon1.Visible = false;
        }
    }
}