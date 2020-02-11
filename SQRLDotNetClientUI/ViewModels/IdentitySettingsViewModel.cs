﻿using Avalonia;
using Avalonia.Controls;
using ReactiveUI;
using SQRLDotNetClientUI.Views;
using SQRLUtilsLib;
using System;
using System.Collections.Generic;

namespace SQRLDotNetClientUI.ViewModels
{
    class IdentitySettingsViewModel : ViewModelBase
    {
        public SQRL SqrlInstance { get; set; }
        public SQRLIdentity Identity { get; set; }
        public SQRLIdentity IdentityCopy { get; set; }

        private bool _canSave = true;
        public bool CanSave { get => _canSave; set => this.RaiseAndSetIfChanged(ref _canSave, value); }

        private double _ProgressPercentage = 0;
        public double ProgressPercentage { get => _ProgressPercentage; set => this.RaiseAndSetIfChanged(ref _ProgressPercentage, value); }

        public double ProgressMax { get; set; } = 100;

        private string _progressText = string.Empty;
        public string ProgressText { get => _progressText; set => this.RaiseAndSetIfChanged(ref _progressText, value); }

        public IdentitySettingsViewModel() { }

        public IdentitySettingsViewModel(SQRL sqrlInstance, SQRLIdentity identity)
        {
            this.Title = "SQRL Client - Identity Settings";
            this.SqrlInstance = sqrlInstance;
            this.Identity = identity;
            this.IdentityCopy = identity.Clone();

            if (identity != null) this.Title += " (" + identity.IdentityName + ")";
        }

        public void Close()
        {
            ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).Content =
                ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).MainMenu;
        }

        public async void Save()
        {
            CanSave = false;

            if (!HasChanges())
            {
                Close();
                CanSave = true;
                return;
            }

            
            InputSecretDialogView passwordDlg = new InputSecretDialogView(SecretType.Password);
            passwordDlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            string password = await passwordDlg.ShowDialog<string>(
                AvaloniaLocator.Current.GetService<MainWindow>());

            if (password == null)
            {
                CanSave = true;
                return;
            }

            var progress = new Progress<KeyValuePair<int, string>>(progress =>
            {
                this.ProgressPercentage = (double)progress.Key;
                this.ProgressText = progress.Value + progress.Key;
            });

            (bool ok, byte[] imk, byte[] ilk) = await SqrlInstance.DecryptBlock1(Identity, password, progress);

            if (!ok)
            {
                var msgBox = MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow(
                    $"Error", $"The identity could not be decrypted using the given password! Please try again!", 
                    MessageBox.Avalonia.Enums.ButtonEnum.Ok, 
                    MessageBox.Avalonia.Enums.Icon.Error);

                await msgBox.ShowDialog(AvaloniaLocator.Current.GetService<MainWindow>());

                ProgressText = "";
                ProgressPercentage = 0;
                CanSave = true;
                return;
            }

            SQRLIdentity id = await SqrlInstance.GenerateIdentityBlock1(
                imk, ilk, password, IdentityCopy, progress, IdentityCopy.Block1.PwdVerifySeconds);

            // Swap out the old type 1 block with the updated one
            // TODO: We should probably make sure that this is an atomic operation
            Identity.Blocks.Remove(Identity.Block1);
            Identity.Blocks.Insert(0, id.Block1);

            CanSave = true;
            Close();
        }

        /// <summary>
        /// Returns <c>true</c> if any of the identity settings were changed by the 
        /// user are and those changes have not been applied yet, or <c>false</c> otherwise.
        /// </summary>
        public bool HasChanges()
        {
            if (Identity.Block1.HintLength != IdentityCopy.Block1.HintLength) return true;
            if (Identity.Block1.PwdTimeoutMins != IdentityCopy.Block1.PwdTimeoutMins) return true;
            if (Identity.Block1.PwdVerifySeconds != IdentityCopy.Block1.PwdVerifySeconds) return true;

            if (Identity.Block1.OptionFlags.CheckForUpdates != IdentityCopy.Block1.OptionFlags.CheckForUpdates) return true;
            if (Identity.Block1.OptionFlags.ClearQuickPassOnIdle != IdentityCopy.Block1.OptionFlags.ClearQuickPassOnIdle) return true;
            if (Identity.Block1.OptionFlags.ClearQuickPassOnSleep != IdentityCopy.Block1.OptionFlags.ClearQuickPassOnSleep) return true;
            if (Identity.Block1.OptionFlags.ClearQuickPassOnSwitchingUser != IdentityCopy.Block1.OptionFlags.ClearQuickPassOnSwitchingUser) return true;
            if (Identity.Block1.OptionFlags.EnableMITMAttackWarning != IdentityCopy.Block1.OptionFlags.EnableMITMAttackWarning) return true;
            if (Identity.Block1.OptionFlags.EnableNoCPSWarning != IdentityCopy.Block1.OptionFlags.EnableNoCPSWarning) return true;
            if (Identity.Block1.OptionFlags.RequestNoSQRLBypass != IdentityCopy.Block1.OptionFlags.RequestNoSQRLBypass) return true;
            if (Identity.Block1.OptionFlags.RequestSQRLOnlyLogin != IdentityCopy.Block1.OptionFlags.RequestSQRLOnlyLogin) return true;
            if (Identity.Block1.OptionFlags.UpdateAutonomously != IdentityCopy.Block1.OptionFlags.UpdateAutonomously) return true;

            return false;
        }
    }
}
