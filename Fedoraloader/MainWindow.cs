using System.Diagnostics;
using System.IO.Compression;
using System.Management.Automation;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Fedoraloader.Injection;
using Microsoft.Win32;

namespace Fedoraloader
{
    public partial class MainWindow : Form
    {
        #region Signatures
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
        (
            int nLeftRect,
            int nTopRect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse
        );
        #endregion

        public const string ACTION_URL = "https://nightly.link/tf2cheater2013/Fedoraware/workflows/msbuild/main/Fedoraware.zip";
        public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        private Point _mouseStartPos;
        private string _workDir;
        private string _fileDir;

        public MainWindow()
        {
            InitializeComponent();
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 15, 15));

            // Check for administrator rights
            if (!IsElevated)
            {
                MessageBox.Show("This programm will not work without administrator rights!\nPlease run it as administrator.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Generate random work folder
            if (string.IsNullOrEmpty(Properties.Settings.Default.folderID))
            {
                do
                {
                    Properties.Settings.Default.folderID = Utils.RandomString(12);
                } while (Directory.Exists(Path.GetTempPath() + Properties.Settings.Default.folderID));
                Properties.Settings.Default.Save();
            }

            _workDir = Path.GetTempPath() + Properties.Settings.Default.folderID;
            _fileDir = _workDir + @"\Data\";
            Debug.WriteLine(_workDir);

            chkBypass.Checked = Properties.Settings.Default.bypass;
            chkDefender.Checked = Properties.Settings.Default.defender;

            Text = Utils.RandomString(8);
        }

        private async void btnLoad_Click(object sender, EventArgs e)
        {
            btnLoad.Enabled = false;

            await Task.Run(() =>
            {
                RunLoader();
                UpdateStatus("", false);
                btnLoad.Invoke(() => { btnLoad.Enabled = true; });
            });
        }

        private void RunLoader()
        {
            // Cleanup and preperation
            UpdateStatus("Preparing files...");
            try
            {
                if (Directory.Exists(_fileDir))
                {
                    Directory.Delete(_fileDir, true);
                }

                Directory.CreateDirectory(_fileDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to create working directory:\n" + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Thread.Sleep(250);

            // Wait for TF2
            UpdateStatus("Searching for TF2...");
            Process? tfProcess = GetGameProcess();
            if (tfProcess == null) { MessageBox.Show("Invalid game process!", "", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            string dllFile = _fileDir + @"Fware(Release).dll";

            // Download latest build
            try
            {
                string dlPath = _fileDir + @"\" + Utils.RandomString(8) + ".zip";

                // Add defender exception if enabled
                if (chkDefender.Checked)
                {
                    UpdateStatus("Adding Defender exception...");
                    if (!AddDefender(Directory.GetCurrentDirectory()) || !AddDefender(_workDir))
                    {
                        if (MessageBox.Show("Failed to add Defender exception!\nDo you want to continue?", "", MessageBoxButtons.YesNo) == DialogResult.No)
                        {
                            return;
                        }
                    }
                }

                Thread.Sleep(250);

                // Download build
                UpdateStatus("Downloading...");
                /*WebClient wc = new();
                wc.DownloadFile(ACTION_URL, dlPath);*/
                
                HttpClientHandler clientHandler = new();
                clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

                using (var httpClient = new HttpClient())
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, ACTION_URL);
                    using var contentStream = httpClient.Send(request).Content.ReadAsStream();
                    using var fileStream = new FileStream(dlPath, FileMode.Create, FileAccess.Write);
                    contentStream.CopyTo(fileStream);
                }

                Thread.Sleep(250);

                // Extract build
                UpdateStatus("Extracting files...");
                ZipFile.ExtractToDirectory(dlPath, _fileDir, true);
                File.Delete(dlPath);

                Thread.Sleep(250);

                dllFile = Directory.GetFiles(_fileDir, "*.dll").First();
            }
            catch (Exception ex)
            {
                if (MessageBox.Show("Failed to download the latest build:\n" + ex.Message, "", MessageBoxButtons.OKCancel, MessageBoxIcon.Error) == DialogResult.Cancel)
                {
                    return;
                }
            }

            // Inject the dll if it exists
            if (File.Exists(dllFile))
            {
                UpdateStatus("Injecting...");
                Inject(tfProcess, dllFile);
            }
            else
            {
                DialogResult dlgResult = MessageBox.Show("The loader file could not be found!\nDo you want to manually select a .dll file?", "", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dlgResult != DialogResult.Yes) { return; }

                OpenFileDialog dllDialog = new();
                dllDialog.Multiselect = false;
                dllDialog.Title = "Select .dll file";
                dllDialog.Filter = "DLL File (*.dll)|*.dll";

                if (dllDialog.ShowDialog() == DialogResult.OK)
                {
                    UpdateStatus("Injecting custom dll...");
                    Inject(tfProcess, dllDialog.FileName);
                }
            }
        }

        private void Inject(Process pProcess, string pFileName)
        {
            Injector tfInjector = new();
            InjectionResult injectionResult = tfInjector.Inject(pProcess.Handle, pFileName);
            switch (injectionResult)
            {
                case InjectionResult.AllocationError:
                    MessageBox.Show("Allocation error!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

                case InjectionResult.CallLoadLibraryError:
                    MessageBox.Show("Call LoadLibrary error!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

                case InjectionResult.HookFunctionsFail:
                    MessageBox.Show("Failed to hook functions!\nMake sure to run the loader as administrator.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

                case InjectionResult.LoadLibraryAddressNotFound:
                    MessageBox.Show("LoadLibrary address not found!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

                case InjectionResult.RestoreHooksFail:
                    MessageBox.Show("Failed to restore hooks!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

                case InjectionResult.SetLoadLibraryPathError:
                    MessageBox.Show("Failed to set LoadLibrary path!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

                case InjectionResult.Success:
                    UpdateStatus("Injection successful!");
                    MessageBox.Show("Success!", "", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                    break;

                default:
                    MessageBox.Show("Unknown error!", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
            }
        }

        private Process? GetGameProcess()
        {
            if (chkBypass.Checked)
            {
                if (!VacBypass())
                {
                    if (MessageBox.Show("VAC bypass failed! Be careful.", "", MessageBoxButtons.OKCancel,
                            MessageBoxIcon.Error) == DialogResult.Cancel)
                    {
                        return null;
                    }
                }
            }

            Process[] procList = Process.GetProcessesByName("hl2");
            if (procList.Length == 0)
            {
                MessageBox.Show("Team Fortress 2 could not be found!\nMake sure that you open it before loading.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            return procList.First();
        }

        private bool VacBypass()
        {
            IEnumerable<Process> killProcs = Process.GetProcesses()
                .Where(p => p.ProcessName is "hl2" or "steam" or "steamwebhelper");

            // Kill all steam processes
            UpdateStatus("Closing Steam...");
            foreach (Process proc in killProcs)
            {
                proc.Kill();
            }

            do
            {
                Thread.Sleep(250);
            } while (Process.GetProcesses().Count(p => p.ProcessName is "hl2" or "steam" or "steamwebhelper") > 0);

            // Extract VAC Bypass
            Directory.CreateDirectory(_workDir + @"\Bypass\");
            File.WriteAllBytes(_workDir + @"\Bypass\VAC-Bypass.dll", Properties.Resources.VAC_Bypass);

            Thread.Sleep(250);

            // Start new steam process
            UpdateStatus("Starting Steam...");
            string steamPath = (string)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", "");
            if (!File.Exists(steamPath))
            {
                return false;
            }

            // Wait for process
            Process steamProcess = new Process();
            steamProcess.StartInfo.FileName = steamPath;
            steamProcess.StartInfo.UseShellExecute = true;
            steamProcess.StartInfo.Arguments = "-applaunch 440";
            steamProcess.StartInfo.Verb = "runas";
            steamProcess.Start();

            bool moduleFound = false;
            do
            {
                foreach (ProcessModule pm in steamProcess.Modules)
                {
                    if (pm.ModuleName == "steam.exe")
                    {
                        moduleFound = true;
                    }
                }
                Thread.Sleep(250);
            } while (!moduleFound);

            UpdateStatus("Loading VAC bypass...");
            Injector steamInjector = new();
            InjectionResult injectionResult = steamInjector.Inject(steamProcess.Handle, _workDir + @"\Bypass\VAC-Bypass.dll");
            if (injectionResult != InjectionResult.Success)
            {
                MessageBox.Show("VAC bypass failed! Could not inject into Steam.", "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            UpdateStatus("Waiting for TF2...");

            do
            {
                Thread.Sleep(1500);
            } while (Process.GetProcesses().Count(p => p.ProcessName is "hl2") == 0);

            return true;
        }

        private bool AddDefender(string pDirectory)
        {
            try
            {
                PowerShell.Create()
                    .AddScript(@"Add-MpPreference -ExclusionPath '" + pDirectory + "'")
                    .Invoke();

                Debug.WriteLine("Added folder to defender: " + pDirectory);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private void UpdateStatus(string pStatus, bool pVisible = true)
        {
            lblStatus.Invoke(() =>
            {
                lblStatus.Visible = pVisible;
                lblStatus.Text = pStatus;
            });
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        #region Draggable Form
        private void lblTitle_MouseDown(object sender, MouseEventArgs e)
        {
            pnlHeader_MouseDown(sender, e);
        }

        private void pnlHeader_MouseDown(object sender, MouseEventArgs e)
        {
            _mouseStartPos = new Point(-e.X, -e.Y);
        }

        private void lblTitle_MouseMove(object sender, MouseEventArgs e)
        {
            pnlHeader_MouseMove(sender, e);
        }

        private void pnlHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Point mousePos = Control.MousePosition;
                mousePos.Offset(_mouseStartPos.X, _mouseStartPos.Y);
                Location = mousePos;
            }
        }
        #endregion

        #region Settings
        private void chkBypass_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.bypass = chkBypass.Checked;
            Properties.Settings.Default.Save();
        }

        private void chkDefender_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.defender = chkDefender.Checked;
            Properties.Settings.Default.Save();
        }
        #endregion
    }
}