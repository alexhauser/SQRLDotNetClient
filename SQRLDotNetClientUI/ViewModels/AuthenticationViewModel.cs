﻿using ReactiveUI;
using Sodium;
using SQRLDotNetClientUI.Views;
using SQRLUtilsLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SQRLDotNetClientUI.Utils;
using MessageBox.Avalonia.Enums;
using MessageBox.Avalonia;
using Avalonia.Controls;
using SQRLDotNetClientUI.Models;
using Serilog;

namespace SQRLDotNetClientUI.ViewModels
{
    public class AuthenticationViewModel : ViewModelBase
    {
        public enum LoginAction
        {
            Login,
            Disable,
            Remove
        };

        private LoginAction action = LoginAction.Login;
        public LoginAction Action
        {
            get => action; 
            set { this.RaiseAndSetIfChanged(ref action, value); }
        }

        private SQRL _sqrlInstance = SQRL.GetInstance(true);
        private QuickPassManager _quickPassManager = QuickPassManager.Instance;

        public Uri Site { get; set; }
        public string AltID { get; set; } = "";

        public string _password = "";
        public string Password
        {
            get => _password;
            set => this.RaiseAndSetIfChanged(ref _password, value);
        }

        public bool AuthAction { get; set; }

        public string _siteID = "";
        public string SiteID 
        { 
            get { return $"{this.Site.Host}"; } 
            set => this.RaiseAndSetIfChanged(ref _siteID, value); 
        }

        public string _passwordLabel = "";
        public string PasswordLabel
        {
            get => _passwordLabel;
            set => this.RaiseAndSetIfChanged(ref _passwordLabel, value);
        }

        public string _identityName = "";
        public string IdentityName
        {
            get => _identityName;
            set => this.RaiseAndSetIfChanged(ref _identityName, value);
        }

        public bool _showIdentitySelector;
        public bool ShowIdentitySelector
        {
            get => _showIdentitySelector;
            set => this.RaiseAndSetIfChanged(ref _showIdentitySelector, value);
        }

        public bool _advancedFunctionsVisible = false;
        public bool AdvancedFunctionsVisible
        {
            get => _advancedFunctionsVisible;
            set => this.RaiseAndSetIfChanged(ref _advancedFunctionsVisible, value);
        }

        public bool _isBusy = false;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private int _Block1Progress = 0;

        public int Block1Progress 
        { 
            get => _Block1Progress; 
            set => this.RaiseAndSetIfChanged(ref _Block1Progress, value); 
        }

        public int MaxProgress { get => 100; }

        public AuthenticationViewModel()
        {
            Init();
            this.Site = new Uri("https://google.com");
            this.SiteID = this.Site.Host;
        }

        public AuthenticationViewModel(Uri site)
        {
            Init();
            this.Site = site;
            this.SiteID = site.Host;
        }

        private void Init()
        {
            this.Title = _loc.GetLocalizationValue("AuthenticationWindowTitle");
            this.IdentityName = _identityManager.CurrentIdentity?.IdentityName;
            _identityManager.IdentityChanged += IdentityChanged;
            _identityManager.IdentityCountChanged += IdentityCountChanged;

            IdentityCountChanged(this, new IdentityCountChangedEventArgs(
                _identityManager.IdentityCount));

            // Observe changes to the password/quickpass field
            // and initiate automatic quickpass login if available
            this.WhenAnyValue(x => x.Password).Subscribe(x =>
            {
                if (!_quickPassManager.HasQuickPass())
                    return;

                int quickPassLength = _identityManager.CurrentIdentity.Block1.HintLength;

                Log.Debug("QuickPassLength len: {QuickPassLength}, CurrentLength: {CurrentLength}",
                    quickPassLength, x.Length);

                if (x.Length == quickPassLength)
                {
                    Log.Information("Initiating login using QuickPass");

                    Login(useQuickPass: true);
                }
            });

            CheckForQuickPass();
        }

        private void CheckForQuickPass()
        {
            if (!_quickPassManager.HasQuickPass())
                this.PasswordLabel = _loc.GetLocalizationValue("PasswordLabel");
            else
                this.PasswordLabel = _loc.GetLocalizationValue("QuickPassLabel");
        }

        private void IdentityCountChanged(object sender, IdentityCountChangedEventArgs e)
        {
            if (e.IdentityCount > 1) this.ShowIdentitySelector = true;
            else this.ShowIdentitySelector = false;
        }

        public async void SwitchIdentity()
        {
            SelectIdentityDialogView selectIdDialog = new SelectIdentityDialogView();
            selectIdDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await selectIdDialog.ShowDialog(_mainWindow);
        }

        private void IdentityChanged(object sender, IdentityChangedEventArgs e)
        {
            this.IdentityName = e.Identity.IdentityName;
        }

        public void ShowAdvancedFunctions()
        {
            this.AdvancedFunctionsVisible = true;
        }

        public void Cancel()
        {
            if (_sqrlInstance.cps.PendingResponse)
            {
                _sqrlInstance.cps.cpsBC.Add(_sqrlInstance.cps.Can);
            }
            while (_sqrlInstance.cps.PendingResponse)
                ;
            _mainWindow.Close();
        }

        public async void Login(bool useQuickPass = false)
        {
            byte[] imk, ilk;
            this.IsBusy = true;

            var progressBlock1 = new Progress<KeyValuePair<int, string>>(percent =>
            {
                this.Block1Progress = (int)percent.Key;
            });

            if (useQuickPass)
            {
                var keys = await _quickPassManager.GetQuickPassDecryptedImk(this.Password, null, progressBlock1);
                imk = keys.Imk;
                ilk = keys.Ilk;
            }
            else
            {
                var result = await SQRL.DecryptBlock1(_identityManager.CurrentIdentity, this.Password, progressBlock1);
                if (!result.Item1)
                {
                    var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandardWindow(
                        _loc.GetLocalizationValue("BadPasswordErrorTitle"),
                        _loc.GetLocalizationValue("BadPasswordError"),
                        ButtonEnum.Ok,
                        Icon.Error);
                    await messageBoxStandardWindow.ShowDialog(_mainWindow);
                    this.IsBusy = false;
                    return;
                }
                imk = result.Item2;
                ilk = result.Item3;
            }

            // Block 1 was sucessfully decrypted using the master pasword,
            // so enable QuickPass if it isn't already set
            if (!_quickPassManager.HasQuickPass(_identityManager.CurrentIdentityUniqueId))
                _quickPassManager.SetQuickPass(this.Password, imk, ilk, _identityManager.CurrentIdentity);

            var siteKvp = SQRL.CreateSiteKey(this.Site, this.AltID, imk);

            Dictionary<byte[], Tuple<byte[], KeyPair>> priorKvps = null;
            priorKvps = GeneratePriorKeyInfo(imk, priorKvps);
            SQRLOptions sqrlOpts = new SQRLOptions(SQRLOptions.SQRLOpts.CPS | SQRLOptions.SQRLOpts.SUK);
            var serverResponse = SQRL.GenerateQueryCommand(this.Site, siteKvp, sqrlOpts, null, 0, priorKvps);

            if (serverResponse.CommandFailed)
            {
                var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandardWindow(
                        _loc.GetLocalizationValue("ErrorTitleGeneric"),
                        _loc.GetLocalizationValue("SQRLCommandFailedUnknown"),
                        ButtonEnum.Ok,
                        Icon.Error);
                await messageBoxStandardWindow.ShowDialog(_mainWindow);

                this.IsBusy = false;
                return;
            }

            // New account, ask if they want to create one
            if (!serverResponse.CurrentIDMatch && !serverResponse.PreviousIDMatch)
            {
                serverResponse = await HandleNewAccount(imk, ilk, siteKvp, sqrlOpts, serverResponse);
            }
            // A previous id matches, replace the outdated id on the server with the latest
            else if (serverResponse.PreviousIDMatch)
            {
                byte[] ursKey = null;
                ursKey = SQRL.GetURSKey(serverResponse.PriorMatchedKey.Key, Utilities.Base64ToBinary(serverResponse.SUK, string.Empty, Utilities.Base64Variant.UrlSafeNoPadding));
                StringBuilder additionalData = null;
                if (!string.IsNullOrEmpty(serverResponse.SIN))
                {
                    additionalData = new StringBuilder();
                    byte[] ids = SQRL.CreateIndexedSecret(this.Site, AltID, imk, Encoding.UTF8.GetBytes(serverResponse.SIN));
                    additionalData.AppendLineWindows($"ins={Utilities.BinaryToBase64(ids, Utilities.Base64Variant.UrlSafeNoPadding)}");
                    byte[] pids = SQRL.CreateIndexedSecret(serverResponse.PriorMatchedKey.Value.Item1, Encoding.UTF8.GetBytes(serverResponse.SIN));
                    additionalData.AppendLineWindows($"pins={Utilities.BinaryToBase64(pids, Utilities.Base64Variant.UrlSafeNoPadding)}");

                }
                serverResponse = SQRL.GenerateIdentCommandWithReplace(serverResponse.NewNutURL, siteKvp, serverResponse.FullServerRequest, 
                    ilk, ursKey, serverResponse.PriorMatchedKey.Value.Item2, sqrlOpts, additionalData);
            }
            // Current id matches 
            else if (serverResponse.CurrentIDMatch)
            {
                int askResponse = 0;
                if (serverResponse.HasAsk)
                {
                    MainWindow w = new MainWindow();

                    var mwTemp = new MainWindowViewModel();
                    w.DataContext = mwTemp;
                    w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    var avm = new AskViewModel(serverResponse)
                    {
                        CurrentWindow = w
                    };
                    mwTemp.Content = avm;
                    askResponse = await w.ShowDialog<int>(_mainWindow);
                }

                StringBuilder addClientData = null;
                if (askResponse > 0)
                {
                    addClientData = new StringBuilder();
                    addClientData.AppendLineWindows($"btn={askResponse}");
                }
                if (serverResponse.SQRLDisabled)
                {
                    var disabledAccountAlert = string.Format(_loc.GetLocalizationValue("SqrlDisabledAlert"), this.SiteID, Environment.NewLine);
                    var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandardWindow(
                        _loc.GetLocalizationValue("ReEnableSQRLTitle").ToUpper(),
                        $"{disabledAccountAlert}",
                        ButtonEnum.YesNo,
                        Icon.Lock);
                    messageBoxStandardWindow.SetMessageStartupLocation(WindowStartupLocation.CenterOwner);
                    var btResult = await messageBoxStandardWindow.ShowDialog(_mainWindow);
                    if (btResult == ButtonResult.Yes)
                    {
                        RetryRescueCode:
                        InputSecretDialogView rescueCodeDlg = new InputSecretDialogView(SecretType.RescueCode);
                        rescueCodeDlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        string rescueCode = await rescueCodeDlg.ShowDialog<string>(
                            _mainWindow);
                                
                        var iukData = await SQRL.DecryptBlock2(_identityManager.CurrentIdentity, SQRL.CleanUpRescueCode(rescueCode),progressBlock1);
                        if (iukData.Item1)
                        {
                            byte[] ursKey = null;
                            ursKey = SQRL.GetURSKey(iukData.Item2, Utilities.Base64ToBinary(serverResponse.SUK, string.Empty, Utilities.Base64Variant.UrlSafeNoPadding));

                            iukData.Item2.ZeroFill();
                            serverResponse = SQRL.GenerateEnableCommand(serverResponse.NewNutURL, siteKvp, serverResponse.FullServerRequest, ursKey, addClientData, sqrlOpts);
                        }
                        else
                        {
                            var msgBox = MessageBoxManager.GetMessageBoxStandardWindow(
                                _loc.GetLocalizationValue("ErrorTitleGeneric"),
                                _loc.GetLocalizationValue("InvalidRescueCodeMessage"),
                                ButtonEnum.YesNo,
                                Icon.Error);
                            var answer =await msgBox.ShowDialog(_mainWindow);
                            if(answer == ButtonResult.Yes)
                            {
                                goto RetryRescueCode;
                            }
                        }
                    }

                }
                //Here
                switch (Action)
                {
                    case LoginAction.Login:
                        {
                            addClientData = GenerateSIN(imk, serverResponse, addClientData);
                            serverResponse = SQRL.GenerateSQRLCommand(SQRLCommands.ident, serverResponse.NewNutURL, siteKvp, serverResponse.FullServerRequest, addClientData, sqrlOpts);
                            if (SQRL.GetInstance(true).cps != null && _sqrlInstance.cps.PendingResponse)
                            {
                                _sqrlInstance.cps.cpsBC.Add(new Uri(serverResponse.SuccessUrl));
                            }
                            while (_sqrlInstance.cps.PendingResponse)
                                ;
                            _mainWindow.Close();
                        }
                        break;
                    case LoginAction.Disable:
                        {
                            var disableAccountAlert = string.Format(_loc.GetLocalizationValue("DisableAccountAlert"), this.SiteID, Environment.NewLine);
                            var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandardWindow(
                                _loc.GetLocalizationValue("WarningMessageBoxTitle").ToUpper(), 
                                $"{disableAccountAlert}", 
                                ButtonEnum.YesNo, 
                                Icon.Lock);
                            messageBoxStandardWindow.SetMessageStartupLocation(Avalonia.Controls.WindowStartupLocation.CenterOwner);
                            var btResult = await messageBoxStandardWindow.ShowDialog(_mainWindow);

                            if (btResult == ButtonResult.Yes)
                            {
                                GenerateSIN(imk, serverResponse, addClientData);
                                serverResponse = SQRL.GenerateSQRLCommand(SQRLCommands.disable, serverResponse.NewNutURL, siteKvp, serverResponse.FullServerRequest, addClientData, sqrlOpts);
                                if (_sqrlInstance.cps != null && _sqrlInstance.cps.PendingResponse)
                                {
                                    _sqrlInstance.cps.cpsBC.Add(_sqrlInstance.cps.Can);
                                }
                                while (_sqrlInstance.cps.PendingResponse)
                                    ;
                                _mainWindow.Close();
                            }
                        }
                        break;
                    case LoginAction.Remove:
                        {
                            InputSecretDialogView rescueCodeDlg = new InputSecretDialogView(SecretType.RescueCode);
                            rescueCodeDlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            string rescueCode = await rescueCodeDlg.ShowDialog<string>(
                                _mainWindow);
                            var rescueResult = await SQRL.DecryptBlock2(_identityManager.CurrentIdentity, SQRL.CleanUpRescueCode(rescueCode), progressBlock1);
                            if (rescueResult.Item1)
                            {
                                byte[] ursKey = SQRL.GetURSKey(rescueResult.Item2, Sodium.Utilities.Base64ToBinary(serverResponse.SUK, string.Empty, Sodium.Utilities.Base64Variant.UrlSafeNoPadding));

                                serverResponse = SQRL.GenerateSQRLCommand(SQRLCommands.remove, serverResponse.NewNutURL, siteKvp, serverResponse.FullServerRequest, addClientData, sqrlOpts, null, ursKey);
                                if (_sqrlInstance.cps != null && _sqrlInstance.cps.PendingResponse)
                                {
                                    _sqrlInstance.cps.cpsBC.Add(_sqrlInstance.cps.Can);
                                }
                                while (_sqrlInstance.cps.PendingResponse)
                                    ;
                                _mainWindow.Close();
                            }
                            else
                            {
                                var msgBox = MessageBoxManager.GetMessageBoxStandardWindow(
                                _loc.GetLocalizationValue("ErrorTitleGeneric"),
                                _loc.GetLocalizationValue("InvalidRescueCodeMessage"),
                                ButtonEnum.Ok,
                                Icon.Error);
                                await msgBox.ShowDialog(_mainWindow);
                            }
                        }
                        break;
                }
            }

            this.IsBusy = false;
        }

        private StringBuilder GenerateSIN(byte[] imk, SQRLServerResponse serverResponse, StringBuilder addClientData)
        {
            if (!string.IsNullOrEmpty(serverResponse.SIN))
            {
                if (addClientData == null)
                    addClientData = new StringBuilder();
                byte[] ids = SQRL.CreateIndexedSecret(this.Site, AltID, imk, Encoding.UTF8.GetBytes(serverResponse.SIN));
                addClientData.AppendLineWindows($"ins={Sodium.Utilities.BinaryToBase64(ids, Utilities.Base64Variant.UrlSafeNoPadding)}");
            }

            return addClientData;
        }

        private async System.Threading.Tasks.Task<SQRLServerResponse> HandleNewAccount(byte[] imk, byte[] ilk, KeyPair siteKvp, SQRLOptions sqrlOpts, SQRLServerResponse serverResponse)
        {
            string newAccountQuestion = string.Format(_loc.GetLocalizationValue("NewAccountQuestion"), this.SiteID);
            string genericQuestionTitle = string.Format(_loc.GetLocalizationValue("GenericQuestionTitle"), this.SiteID);

            var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandardWindow(
                $"{genericQuestionTitle}", 
                $"{newAccountQuestion}", 
                ButtonEnum.YesNo, 
                Icon.Plus);

            messageBoxStandardWindow.SetMessageStartupLocation(Avalonia.Controls.WindowStartupLocation.CenterOwner);
            var btnRsult = await messageBoxStandardWindow.ShowDialog(_mainWindow);

            if (btnRsult == ButtonResult.Yes)
            {
                StringBuilder additionalData = null;
                if (!string.IsNullOrEmpty(serverResponse.SIN))
                {
                    additionalData = new StringBuilder();
                    byte[] ids = SQRL.CreateIndexedSecret(this.Site, AltID, imk, Encoding.UTF8.GetBytes(serverResponse.SIN));
                    additionalData.AppendLineWindows($"ins={Sodium.Utilities.BinaryToBase64(ids, Utilities.Base64Variant.UrlSafeNoPadding)}");
                }
                serverResponse = SQRL.GenerateNewIdentCommand(serverResponse.NewNutURL, siteKvp, serverResponse.FullServerRequest, ilk, sqrlOpts, additionalData);
                if (!serverResponse.CommandFailed)
                {
                    if (_sqrlInstance.cps.PendingResponse)
                    {
                        _sqrlInstance.cps.cpsBC.Add(new Uri(serverResponse.SuccessUrl));
                    }
                    while (_sqrlInstance.cps.PendingResponse)
                        ;
                    _mainWindow.Close();
                }
            }
            else
            {
                if (_sqrlInstance.cps.PendingResponse)
                {
                    _sqrlInstance.cps.cpsBC.Add(_sqrlInstance.cps.Can);
                }
                while (_sqrlInstance.cps.PendingResponse)
                    ;
                _mainWindow.Close();
            }

            return serverResponse;
        }

        private Dictionary<byte[], Tuple<byte[], KeyPair>> GeneratePriorKeyInfo(byte[] imk, Dictionary<byte[], Tuple<byte[], KeyPair>> priorKvps)
        {
            if (_identityManager.CurrentIdentity.Block3 != null && _identityManager.CurrentIdentity.Block3.Edition > 0)
            {
                byte[] decryptedBlock3 = SQRL.DecryptBlock3(imk, _identityManager.CurrentIdentity, out bool allGood);
                List<byte[]> oldIUKs = new List<byte[]>();
                if (allGood)
                {
                    int skip = 0;
                    int ct = 0;
                    while (skip < decryptedBlock3.Length)
                    {
                        oldIUKs.Add(decryptedBlock3.Skip(skip).Take(32).ToArray());
                        skip += 32;
                        ;
                        if (++ct >= 3)
                            break;
                    }

                    SQRL.ZeroFillByteArray(ref decryptedBlock3);
                    priorKvps = SQRL.CreatePriorSiteKeys(oldIUKs, this.Site, AltID);
                    oldIUKs.Clear();
                }
            }

            return priorKvps;
        }
    }
}
