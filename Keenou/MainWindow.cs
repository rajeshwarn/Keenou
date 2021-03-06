﻿/*
 * Keenou
 * Copyright (C) 2015  Charles Munson
 * 
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License along
 * with this program; if not, write to the Free Software Foundation, Inc.,
 * 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
*/

using System;
using System.Windows.Forms;
using System.Security.Principal;
using System.IO;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;
using System.Diagnostics;
using System.Collections.Generic;

namespace Keenou
{
    public partial class MainWindow : Form
    {
        // General random number generator instance 
        private static readonly SecureRandom Random = new SecureRandom();

        // Various globals used throughout routines 
        protected string defaultVolumeLoc = string.Empty;
        protected string homeFolder = string.Empty;
        protected string username = string.Empty;
        protected string usrSID = string.Empty;
        protected long homeDirSize = 0;




        // Constructor //
        public MainWindow()
        {
            InitializeComponent();


            // Get user name
            this.username = Environment.UserName.ToString();

            // Get user SID
            NTAccount acct = new NTAccount(username);
            SecurityIdentifier s = (SecurityIdentifier)acct.Translate(typeof(SecurityIdentifier));
            this.usrSID = s.ToString();

            // Get user home directory 
            this.homeFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Get volume location (default) 
            this.defaultVolumeLoc = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) + this.username + ".hc";




            // Figure out where the home folder's encrypted file is located for this user //
            string encDrive = (string)Registry.GetValue(Config.LOCAL_MACHINE_REG_ROOT + this.usrSID, "encDrive", string.Empty);
            string encContainerLoc = (string)Registry.GetValue(Config.LOCAL_MACHINE_REG_ROOT + this.usrSID, "encContainerLoc", string.Empty);
            if (!string.IsNullOrWhiteSpace(encContainerLoc) && !string.IsNullOrWhiteSpace(encDrive) && Directory.Exists(encDrive + @":\"))
            {
                // We're already running in an encrypted home directory environment! 
                g_tabContainer.Controls[0].Enabled = false;
                l_homeAlreadyEncrypted.Visible = true;
                l_homeAlreadyEncrypted.Enabled = true;
            }
            // * //



            l_statusLabel.Text = "Ready ...";
            Application.DoEvents();
        }
        // * //



        // Internal events  // 
        private void MainWindow_Load(object sender, EventArgs e)
        {
            // Populate drop downs 
            c_cipher.Items.AddRange(Config.CIPHERS_S);
            c_hash.Items.AddRange(Config.HASHES_S);


            // Choose defaults  
            c_cipher.SelectedIndex = (int)Config.CIPHER_C_DEFAULT;
            c_hash.SelectedIndex = (int)Config.HASH_C_DEFAULT;


            // Fill out user name and SID
            t_userName.Text = this.username;
            t_sid.Text = this.usrSID;


            // Set output volume location
            t_volumeLoc.Text = this.defaultVolumeLoc;

        }
        // * //



        // When user hits the encrypt button for Home Folder //
        private void ReportEncryptHomeError(BooleanResult res)
        {
            if (res.Message != null)
            {
                MessageBox.Show(res.Message);
            }

            // Reset state of window, and display error conditions 
            this.Cursor = Cursors.Default;
            g_tabContainer.Controls[0].Enabled = true;
            l_statusLabel.Text = "ERROR";
            s_progress.Value = 0;
            s_progress.Visible = false;
        }
        private void b_encrypt_Click(object sender, EventArgs e)
        {

            // Sanity checks //
            if (string.IsNullOrWhiteSpace(t_volumeSize.Text))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please specify a volume size!" });
                return;
            }
            if (t_password.Text.Length <= 0 || !string.Equals(t_password.Text, t_passwordConf.Text))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Passwords provided must match and be non-zero in length!" });
                return;
            }
            if (t_volumeLoc.Text.Length <= 0)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please specify a encrypted volume location!" });
                return;
            }
            if (t_volumeLoc.Text.Contains(this.homeFolder))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "You cannot store your encrypted home volume in your home directory!" });
                return;
            }
            if (c_hash.SelectedIndex < 0)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please choose a hash!" });
                return;
            }
            if (c_cipher.SelectedIndex < 0)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please choose a cipher!" });
                return;
            }
            // TODO: warn user if volume size will not fit home directory
            // * //



            // Helper result object
            BooleanResult res = null;


            // Get user-specified values 
            string hashChosen = c_hash.SelectedItem.ToString();
            string cipherChosen = c_cipher.SelectedItem.ToString();
            long volSize = Int64.Parse(t_volumeSize.Text);


            // Progress bar 
            s_progress.Value = 0;
            s_progress.Visible = true;
            Application.DoEvents();
            s_progress.ProgressBar.Refresh();


            // Ensure there will be enough space for the enc volume
            if (volSize > Toolbox.GetAvailableFreeSpace(t_volumeLoc.Text))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "ERROR: Your encrypted volume will not fit on the chosen target drive!" });
                return;
            }



            // Disable while we calcualte stuff 
            this.Cursor = Cursors.WaitCursor;
            g_tabContainer.Controls[0].Enabled = false;



            // GET NEXT FREE DRIVE LETTER 
            string targetDrive = Toolbox.GetNextFreeDriveLetter();
            if (targetDrive == null)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "ERROR: Cannot find a free drive letter!" });
                return;
            }
            // * //



            // Generate master key & protect with user password //
            l_statusLabel.Text = "Generating encryption key ...";
            Application.DoEvents();

            string masterKey = Toolbox.GenerateKey(Config.MASTERKEY_PW_CHAR_COUNT);
            string encMasterKey = Toolbox.PasswordEncryptKey(masterKey, t_password.Text);

            // Ensure we got good stuff back 
            if (masterKey == null || encMasterKey == null)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "ERROR: Cannot generate master key!" });
                return;
            }
            // * //




            // Run work-heavy tasks in a separate thread 
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken cancelToken = cts.Token;
            var workerThread = Task.Factory.StartNew(() =>
            {

                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 17;
                    l_statusLabel.Text = "Creating encrypted volume ...";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Create new encrypted volume //
                res = EncryptDirectory.CreateEncryptedVolume(hashChosen, t_volumeLoc.Text, masterKey, cipherChosen, volSize);
                if (res == null || !res.Success)
                {
                    return res;
                }
                // * //



                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 33;
                    l_statusLabel.Text = "Mounting encrypted volume ...";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Mount home folder's encrypted file as targetDrive //
                res = EncryptDirectory.MountEncryptedVolume(hashChosen, t_volumeLoc.Text, targetDrive, masterKey);
                if (res == null || !res.Success)
                {
                    return res;
                }
                // * //



                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 50;
                    l_statusLabel.Text = "Copying home directory to encrypted container ...";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Copy everything over from home directory to encrypted container //
                res = EncryptDirectory.CopyDataFromHomeFolder(this.homeFolder, targetDrive);
                if (res == null || !res.Success)
                {
                    return res;
                }
                // * //



                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 67;
                    l_statusLabel.Text = "Unmounting encrypted drive ...";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // unmount so we can mount upon login 
                res = EncryptDirectory.UnmountEncryptedVolume(targetDrive);
                if (res == null || !res.Success)
                {
                    return res;
                }
                // * //



                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 84;
                    l_statusLabel.Text = "Installing Keenou-pGina ...";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Install Keenou-pGina 
                using (Process process = new Process())
                {

                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    try
                    {
                        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        startInfo.FileName = "cmd.exe";
                        startInfo.Arguments = "/C \"\"" + Config.KeenouProgramDirectory + "\\Keenou-pGina-setup.exe\"\"";
                        process.StartInfo = startInfo;
                        process.Start(); // this may take a while! 
                        process.WaitForExit();

                        // Ensure no errors were thrown 
                        if (process.ExitCode > 0)
                        {
                            return new BooleanResult() { Success = false, Message = "ERROR: Error while installing Keenou-pGina!" };
                        }
                    }
                    catch (Exception err)
                    {
                        return new BooleanResult() { Success = false, Message = "ERROR: Failed to install Keenou-pGina. " + err.Message };
                    }
                }


                return new BooleanResult() { Success = true };

            }, TaskCreationOptions.LongRunning);



            // When threaded tasks finish, check for errors and continue (if appropriate) 
            workerThread.ContinueWith((antecedent) =>
            {
                // Block until we get a result back from previous thread
                BooleanResult result = antecedent.Result;


                // Check if there was an error in previous thread
                if (result == null || !result.Success)
                {
                    ReportEncryptHomeError(result);
                    return;
                }



                // Set necessary registry values //
                Registry.SetValue(Config.LOCAL_MACHINE_REG_ROOT + this.usrSID, "encContainerLoc", t_volumeLoc.Text);
                Registry.SetValue(Config.LOCAL_MACHINE_REG_ROOT + this.usrSID, "firstBoot", true, RegistryValueKind.DWord);
                Registry.SetValue(Config.LOCAL_MACHINE_REG_ROOT + this.usrSID, "hash", hashChosen);
                Registry.SetValue(Config.LOCAL_MACHINE_REG_ROOT + this.usrSID, "encHeader", encMasterKey);
                // * //



                // Re-enable everything //
                this.Cursor = Cursors.Default;
                l_statusLabel.Text = "Log out and back in to finish ...";
                s_progress.Value = 100;
                Application.DoEvents();
                // * //



                // Inform user of the good news 
                MessageBox.Show("Almost done!  You must log out and log back in via Keenou-pGina to finish the migration!");

            },
            cancelToken,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext()
            );

        }
        // * //



        // When user hits "Choose" box to override default volume location //
        private void b_volumeLoc_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog outputFileDialog = new OpenFileDialog())
            {
                outputFileDialog.InitialDirectory = this.defaultVolumeLoc;
                outputFileDialog.FilterIndex = 0;
                outputFileDialog.CheckFileExists = false;
                outputFileDialog.RestoreDirectory = true;

                try
                {
                    if (outputFileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        t_volumeLoc.Text = outputFileDialog.FileName;

                        if (File.Exists(outputFileDialog.FileName))
                        {
                            MessageBox.Show("Warning: File already exists and will be overwritten!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("[" + ex.GetType().Name + "] " + ex.Message + ex.StackTrace);
                }
            }
        }
        // * //



        // When user hits "Encrypt" button for Cloud service //
        private void ReportEncryptCloudError(BooleanResult res)
        {
            if (res.Message != null)
            {
                MessageBox.Show(res.Message);
            }

            // Reset state of window, and display error conditions 
            this.Cursor = Cursors.Default;
            g_tabContainer.Controls[1].Enabled = true;
            l_statusLabel.Text = "ERROR";
            s_progress.Value = 0;
            s_progress.Visible = false;
        }
        private void b_cloudAction_Click(object sender, EventArgs e)
        {
            // Determine which type of cloud service they want to perform action on 
            Config.Clouds cloudSelected;
            if (rb_cloud_Google.Checked)
            {
                cloudSelected = Config.Clouds.GoogleDrive;
            }
            else if (rb_cloud_OneDrive.Checked)
            {
                cloudSelected = Config.Clouds.OneDrive;
            }
            else if (rb_cloud_Dropbox.Checked)
            {
                cloudSelected = Config.Clouds.Dropbox;
            }
            else
            {
                ReportEncryptCloudError(new BooleanResult() { Success = false, Message = "ERROR: Unsupported cloud type selected!" });
                return;
            }
            // * //



            // Figure out where the cloud's folder is on this computer //
            string cloudPath = EncryptFS.GetCloudServicePath(cloudSelected);
            if (cloudPath == null)
            {
                ReportEncryptCloudError(new BooleanResult() { Success = false, Message = "ERROR: Cannot determine the location of your cloud service!" });
                return;
            }
            // * //



            // Find guid of desired cloud folder //
            string guid = null;
            RegistryKey OurKey = Registry.CurrentUser;
            OurKey = OurKey.OpenSubKey(@"Software\Keenou\drives");
            if (OurKey != null)
            {
                foreach (string Keyname in OurKey.GetSubKeyNames())
                {
                    RegistryKey key = OurKey.OpenSubKey(Keyname);
                    if (key.GetValue("encContainerLoc") != null && key.GetValue("encContainerLoc").ToString() == cloudPath)
                    {
                        guid = Keyname.ToString();
                    }
                }
            }
            // * //


            // Helper result object
            BooleanResult res = null;


            // If there is no registered guid yet, then it's the first time (set up) // 
            if (guid == null)
            {
                res = this.encryptCloud(cloudSelected, cloudPath);
                if (res == null || !res.Success)
                {
                    ReportEncryptCloudError(res);
                    return;
                }
                return;
            }
            // * //



            // Check to see if it is mounted //
            Dictionary<string, string> mounts = EncryptFS.GetAllMountedEncFS();
            if (mounts == null)
            {
                ReportEncryptCloudError(new BooleanResult() { Success = false, Message = "ERROR: Cannot figure out which EncFS instances are mounted!" });
                return;
            }
            if (mounts.ContainsKey(guid))
            {
                // already mounted -- unmount 
                res = this.unmountCloud(guid);
                if (res == null || !res.Success)
                {
                    ReportEncryptCloudError(res);
                    return;
                }
                return;
            }
            else
            {
                // not yet mounted -- mount 
                res = this.mountCloud(guid, cloudSelected, cloudPath);
                if (res == null || !res.Success)
                {
                    ReportEncryptCloudError(res);
                    return;
                }
                return;
            }
            // * //


            // We should never make it to here 
        }

        private BooleanResult encryptCloud(Config.Clouds cloudSelected, string cloudPath)
        {

            // Get new password from user //
            var promptPassword = new PromptNewPassword();
            if (promptPassword.ShowDialog() != DialogResult.OK)
            {
                // Cancelled 
                return new BooleanResult() { Success = true };
            }
            string password = promptPassword.CloudPassword;
            string passwordConf = promptPassword.CloudPasswordConfirm;
            // * //



            // Sanity checks //
            if (password.Length < Config.MIN_PASSWORD_LEN || !string.Equals(password, passwordConf))
            {
                return new BooleanResult() { Success = false, Message = "Passwords provided must match and be non-zero in length!" };
            }
            // * //



            // GET NEXT FREE DRIVE LETTER 
            string targetDrive = Toolbox.GetNextFreeDriveLetter();
            if (targetDrive == null)
            {
                return new BooleanResult() { Success = false, Message = "ERROR: Cannot find a free drive letter!" };
            }
            // * //



            // Microsoft OneDrive will cause problems during migration if it is running, 
            //  so terminate it before we start 
            if (cloudSelected == Config.Clouds.OneDrive)
            {
                // Ask user permission to close process first 
                var confirmResult = MessageBox.Show("OneDrive process must be stopped before migration.", "Should I close it?", MessageBoxButtons.YesNo);
                if (confirmResult != DialogResult.Yes)
                {
                    return new BooleanResult() { Success = false, Message = "ERROR: Please close OneDrive application before migration." };
                }


                // Try OneDrive first, then SkyDrive
                Process[] processes = Process.GetProcessesByName("OneDrive");
                if (processes.Length == 0)
                {
                    processes = Process.GetProcessesByName("SkyDrive");
                }

                // If we found a OneDrive/SkyDrive process running, attempt to close it, or kill it otherwise
                if (processes.Length > 0)
                {
                    processes[0].CloseMainWindow();
                    processes[0].WaitForExit(5000);
                    if (!processes[0].HasExited)
                    {
                        processes[0].Kill();
                        processes[0].WaitForExit(5000);
                        if (!processes[0].HasExited)
                        {
                            return new BooleanResult() { Success = false, Message = "ERROR: Could not close OneDrive application!" };
                        }
                    }
                }
            }
            else
            {
                // Tell user to turn of syncing for service 
                var confirmResult = MessageBox.Show("Please remember to disable file syncronization for " + cloudSelected.ToString(), "Press OK when you're ready.", MessageBoxButtons.OKCancel);
                if (confirmResult != DialogResult.OK)
                {
                    return new BooleanResult() { Success = false, Message = "ERROR: Please disable file synchronization before migration." };
                }
            }
            // * //



            // Disable while we calcualte stuff 
            this.Cursor = Cursors.WaitCursor;
            g_tabContainer.Controls[1].Enabled = false;


            // Progress bar 
            s_progress.Value = 0;
            s_progress.Visible = true;
            Application.DoEvents();
            s_progress.ProgressBar.Refresh();


            // Generate a new GUID to identify this FS
            string guid = Guid.NewGuid().ToString();


            // Helper result object
            BooleanResult res = null;



            // Run work-heavy tasks in a separate thread 
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken cancelToken = cts.Token;
            var workerThread = Task.Factory.StartNew(() =>
            {

                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 25;
                    l_statusLabel.Text = "Generating encryption key ...";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Generate master key & protect with user password //
                string masterKey = Toolbox.GenerateKey(Config.MASTERKEY_PW_CHAR_COUNT);
                string encMasterKey = Toolbox.PasswordEncryptKey(masterKey, password);

                // Ensure we got good stuff back 
                if (masterKey == null)
                {
                    return new BooleanResult() { Success = false, Message = "ERROR: Cannot generate master key!" };
                }
                if (encMasterKey == null)
                {
                    return new BooleanResult() { Success = false, Message = "ERROR: Cannot encrypt master key!" };
                }

                Registry.SetValue(Config.CURR_USR_REG_DRIVE_ROOT + guid, "encHeader", encMasterKey);
                // * //



                // Generate temporary location to hold enc data
                string tempFolderName = cloudPath + ".backup-" + Path.GetRandomFileName();
                Directory.CreateDirectory(tempFolderName);



                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 50;
                    l_statusLabel.Text = "Creating EncFS drive";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Create new EncFS
                res = EncryptFS.CreateEncryptedFS(guid, cloudPath, targetDrive, masterKey, "Secure " + cloudSelected.ToString(), true);
                if (res == null || !res.Success)
                {
                    return res;
                }
                // * //



                // Update UI 
                this.Invoke((MethodInvoker)delegate
                {
                    s_progress.Value = 75;
                    l_statusLabel.Text = "Copying data from Cloud folder to encrypted drive";
                    Application.DoEvents();
                    s_progress.ProgressBar.Refresh();
                });

                // Copy cloud data over 
                res = EncryptFS.MoveDataFromFolder(cloudPath, tempFolderName);
                if (res == null || !res.Success)
                {
                    return res;
                }

                res = EncryptFS.CopyDataFromFolder(tempFolderName, targetDrive + ":\\");
                if (res == null || !res.Success)
                {
                    return res;
                }
                // * //


                return new BooleanResult() { Success = true };

            }, TaskCreationOptions.LongRunning);


            // When threaded tasks finish, check for errors and continue (if appropriate) 
            workerThread.ContinueWith((antecedent) =>
            {
                // Block until we get a result back from previous thread
                BooleanResult result = antecedent.Result;


                // Check if there was an error in previous thread
                if (result == null || !result.Success)
                {
                    ReportEncryptCloudError(result);
                    return;
                }



                // Re-enable everything //
                this.Cursor = Cursors.Default;
                g_tabContainer.Controls[1].Enabled = true;
                l_statusLabel.Text = "Successfully moved your cloud folder!";
                s_progress.Value = 0;
                s_progress.Visible = false;
                Application.DoEvents();
                // * //

            },
            cancelToken,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext()
            );


            return new BooleanResult() { Success = true };
        }

        private BooleanResult mountCloud(string guid, Config.Clouds cloudSelected, string cloudPath)
        {
            // Get password from user //
            var promptPassword = new PromptPassword();
            if (promptPassword.ShowDialog() != DialogResult.OK)
            {
                // Cancelled 
                return new BooleanResult() { Success = true };
            }
            string password = promptPassword.CloudPassword;
            // * //


            // GET NEXT FREE DRIVE LETTER 
            string targetDrive = Toolbox.GetNextFreeDriveLetter();
            if (targetDrive == null)
            {
                return new BooleanResult() { Success = false, Message = "ERROR: Cannot find a free drive letter!" };
            }
            // * //


            // Helper result object
            BooleanResult res = null;



            // Check to see if it is already mounted //
            Dictionary<string, string> mounts = EncryptFS.GetAllMountedEncFS();
            if (mounts == null)
            {
                return new BooleanResult() { Success = false, Message = "ERROR: Cannot figure out which EncFS instances are mounted!" };
            }
            if (mounts.ContainsKey(guid))
            {
                return new BooleanResult() { Success = false, Message = "This encrypted folder appears to already be mounted!" };
            }
            // * //



            // Get and decrypt user's master key (using user password) //
            string masterKey = null;
            string encHeader = (string)Registry.GetValue(Config.CURR_USR_REG_DRIVE_ROOT + guid, "encHeader", null);
            if (string.IsNullOrEmpty(encHeader))
            {
                return new BooleanResult() { Success = false, Message = "ERROR: User's header information could not be found!" };
            }

            masterKey = Toolbox.PasswordDecryptKey(encHeader, password);

            // Make sure we got a key back 
            if (masterKey == null)
            {
                return new BooleanResult() { Success = false, Message = "ERROR: Failed to decrypt master key!" };
            }
            // * //



            // Mount their freshly-created encrypted drive 
            res = EncryptFS.MountEncryptedFS(guid, targetDrive, masterKey, "Secure " + cloudSelected.ToString());
            if (res == null || !res.Success)
            {
                return res;
            }
            // * //


            return new BooleanResult() { Success = true };
        }

        private BooleanResult unmountCloud(string guid)
        {

            // Helper result object
            BooleanResult res = null;


            // Check to see if it is mounted //
            Dictionary<string, string> mounts = EncryptFS.GetAllMountedEncFS();
            if (mounts == null)
            {
                return new BooleanResult() { Success = false, Message = "ERROR: Cannot figure out which EncFS instances are mounted!" };
            }
            if (!mounts.ContainsKey(guid))
            {
                return new BooleanResult() { Success = false, Message = "This encrypted folder does not appear to be mounted!" };
            }
            // * //



            // Determine where this cloud is mounted to //
            string targetDrive = (string)Registry.GetValue(Config.CURR_USR_REG_DRIVE_ROOT + guid, "encDrive", null);
            if (string.IsNullOrEmpty(targetDrive))
            {
                return new BooleanResult() { Success = false, Message = "ERROR: Target drive not found! Is cloud mounted?" };
            }
            // * //



            // Unmount encrypted drive 
            res = EncryptFS.UnmountEncryptedFS(targetDrive);
            if (res == null || !res.Success)
            {
                return res;
            }
            // * //


            return new BooleanResult() { Success = true };
        }
        // * //



        // User wants us to suggest a volume size to them //
        private void b_setVolumeSize_Click(object sender, EventArgs e)
        {
            // Disable while we calcualte stuff 
            this.Cursor = Cursors.WaitCursor;
            g_tabContainer.Controls[0].Enabled = false;
            l_statusLabel.Text = "Calculating your home directory size  ...";
            Application.DoEvents();


            // Do calculation of current size (if not already done) 
            if (this.homeDirSize <= 0)
            {
                var taskA = Task.Factory.StartNew(() => Toolbox.GetDirectorySize(this.homeFolder), TaskCreationOptions.LongRunning);
                CancellationTokenSource cts = new CancellationTokenSource();
                CancellationToken cancelToken = cts.Token;

                taskA.ContinueWith((antecedent) =>
                {
                    this.homeDirSize = antecedent.Result;
                    this.b_setVolumeSize_Click_Callback();
                },
                cancelToken,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext()
                );
            }
            else
            {
                this.b_setVolumeSize_Click_Callback();
            }
        }

        private void b_setVolumeSize_Click_Callback()
        {
            // Determine free space on enc volume target drive 
            long targetSpace = Toolbox.GetAvailableFreeSpace(t_volumeLoc.Text);


            // Show suggested volume size 
            long volSizeSuggested = (Config.VOLUME_SIZE_MULT_DEFAULT * this.homeDirSize / (1024 * 1024));
            t_volumeSize.Text = volSizeSuggested.ToString();


            // If not enough space, alert user
            if (volSizeSuggested >= targetSpace)
            {
                string targetDrive = Path.GetPathRoot(t_volumeLoc.Text);
                MessageBox.Show("Warning: There is not enough space on the " + targetDrive + " drive! ");
            }


            // Re-enable everything 
            this.Cursor = Cursors.Default;
            g_tabContainer.Controls[0].Enabled = true;
            l_statusLabel.Text = "Ready ...";
            Application.DoEvents();
        }
        // * //



        // MENU ITEMS //

        // User clicks the "About" menu item 
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox ab = new AboutBox();
            ab.ShowDialog();
        }

        // User clicks "Add New Personal Folder" menu item
        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddPersonalFolder pf = new AddPersonalFolder();
            pf.ShowDialog();
        }
        // * //



    } // End MainWindow class 

    // End namespace 
}
