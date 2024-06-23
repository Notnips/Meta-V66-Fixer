using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace MetaV66_Fix
{
    public partial class Form1 : Form
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        private BackgroundWorker backgroundWorker;

        public Form1()
        {
            InitializeComponent();
            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            button1.Visible = false;
            if (!IsAdministrator())
            {
                MessageBox.Show("This application needs to be run as an administrator.", "Administrator Privileges Required", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button1.Visible = true;
                return;
            }

            progressBar1.Visible = true;
            progressBar1.Update();
            label1.Text = "Shutting down OVRService...";
            label1.Update();
            StopService("OVRService");

            // Get the Oculus installation path from the registry
            string oculusPath = GetOculusPathFromRegistry();
            if (oculusPath == null)
            {
                MessageBox.Show("Oculus installation path not found in registry.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button1.Visible = true;
                return;
            }

            // Move up one directory from Software to Oculus
            oculusPath = Directory.GetParent(oculusPath)?.FullName;
            if (oculusPath == null)
            {
                MessageBox.Show("Error determining the Oculus directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button1.Visible = true;
                return;
            }

            string supportPath = Path.Combine(oculusPath, "Support");
            string runtimePath = Path.Combine(supportPath, "oculus-runtime");
            string oldRuntimePath = Path.Combine(supportPath, "oculus-runtime_oldV66");

            if (Directory.Exists(oldRuntimePath) && Directory.Exists(runtimePath))
            {
                Directory.Delete(runtimePath, true);
            }
            else if (Directory.Exists(runtimePath))
            {
                Directory.Move(runtimePath, oldRuntimePath);
            }

            label1.Text = "Unpacking oculus-runtime...";
            label1.Update();

            // Start the background worker
            backgroundWorker.RunWorkerAsync(supportPath);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (backgroundWorker.IsBusy)
            {
                backgroundWorker.CancelAsync();
            }
            else
            {
                Application.Exit();
            }
        }

        private bool IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        private void StopService(string serviceName)
        {
            ServiceController service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped);
            }
        }

        private void StartService(string serviceName)
        {
            ServiceController service = new ServiceController(serviceName);
            if (service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running);
            }
        }

        private string GetOculusPathFromRegistry()
        {
            string oculusPath = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Oculus VR, LLC\Oculus\Libraries"))
                {
                    if (key != null)
                    {
                        string subKeyName = key.GetSubKeyNames().FirstOrDefault();
                        if (subKeyName != null)
                        {
                            using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey != null)
                                {
                                    oculusPath = subKey.GetValue("OriginalPath") as string;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading registry: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return oculusPath;
        }

        private void ExtractEmbeddedResourceThreaded(string outputPath)
        {
            string resourceName = "MetaV66_Fix.oculus-runtime.7z";

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Resource not found: " + resourceName);
                }

                using (var archive = SevenZipArchive.Open(stream))
                {
                    int entryCount = archive.Entries.Count;
                    int progress = 0;

                    foreach (var entry in archive.Entries)
                    {
                        if (backgroundWorker.CancellationPending)
                        {
                            return;
                        }

                        if (!entry.IsDirectory)
                        {
                            string entryPath = Path.Combine(outputPath, entry.Key);
                            Directory.CreateDirectory(Path.GetDirectoryName(entryPath));
                            entry.WriteToFile(entryPath, new ExtractionOptions { Overwrite = true });

                            progress++;
                            int percentComplete = (int)((float)progress / entryCount * 100);
                            backgroundWorker.ReportProgress(percentComplete);
                            if (progress == 75)
                            {
                                label1.Text = "Almost Done...";
                                label1.Update();
                            }
                        }
                        else
                        {
                            progress++;
                        }
                    }
                }
            }
        }

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            ExtractEmbeddedResourceThreaded(e.Argument as string);
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar1.Value = e.ProgressPercentage;
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                label1.Text = "Operation was canceled.";
            }
            else if (e.Error != null)
            {
                label1.Text = "An error occurred: " + e.Error.Message;
            }
            else
            {
                label1.Text = "Fix is applied, enjoy!";
            }

            progressBar1.Visible = false;
            button1.Visible = true;
            button2.Visible = true;
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
