﻿using System;
using Avalonia.Controls;

using ReactiveUI;
using SQRLDotNetClientUI.Views;
using SQRLUtilsLib;

namespace SQRLDotNetClientUI.ViewModels
{
    public class ImportIdentityViewModel: ViewModelBase
    {       
        private string _textualIdentity = "";
        public string TextualIdentity 
        { 
            get => _textualIdentity; 
            set { this.RaiseAndSetIfChanged(ref _textualIdentity, value); } 
        }

        private string _identityFile="";
        public string IdentityFile 
        {
            get => _identityFile; 
            set { this.RaiseAndSetIfChanged(ref _identityFile, value); } 
        }

        private bool _canImport = false;
        public bool CanImport
        {
            get => _canImport;
            set { this.RaiseAndSetIfChanged(ref _canImport, value); }
        }

        public ImportIdentityViewModel()
        {
            this.Title = _loc.GetLocalizationValue("ImportIdentityWindowTitle");

            this.WhenAnyValue(x => x.TextualIdentity, x => x.IdentityFile,
                  (te, id) => new Tuple<string, string>(te, id))
                .Subscribe((x) => 
                {
                    if (!string.IsNullOrEmpty(x.Item1) || !string.IsNullOrEmpty(x.Item2)) CanImport = true;
                    else CanImport = false;
                });
        }

        public async void ImportFile()
        {
            FileDialogFilter fdf = new FileDialogFilter();
            fdf.Extensions.Add("sqrl");
            fdf.Extensions.Add("sqrc");
            fdf.Name = _loc.GetLocalizationValue("FileDialogFilterName");

            OpenFileDialog ofd = new OpenFileDialog
            {
                AllowMultiple = false
            };
            ofd.Filters.Add(fdf);
            ofd.Title = _loc.GetLocalizationValue("ImportOpenFileDialogTitle");

            var files = await ofd.ShowAsync(_mainWindow);

            if(files != null && files.Length>0)
            {
                this.IdentityFile = files[0];
            }
        }

        public void Cancel()
        {
            ((MainWindowViewModel)_mainWindow.DataContext).Content = 
                ((MainWindowViewModel)_mainWindow.DataContext).PriorContent;
        }

        public  async void ImportVerify()
        {
            SQRLIdentity identity = null;
            if (!string.IsNullOrEmpty(this.TextualIdentity))
            {
                try
                {
                    byte[] identityBytes = SQRL.Base56DecodeIdentity(this.TextualIdentity);
                    bool noHeader = !SQRLIdentity.HasHeader(identityBytes);
                    identity = SQRLIdentity.FromByteArray(identityBytes, noHeader);
                }
                catch (Exception ex)
                {
                    await new MessageBoxViewModel(_loc.GetLocalizationValue("ErrorTitleGeneric"),
                        string.Format(_loc.GetLocalizationValue("TextualImportErrorMessage"), ex.Message),
                        MessageBoxSize.Medium, MessageBoxButtons.OK, MessageBoxIcons.ERROR)
                        .ShowDialog(this);
                }
            }
            else if (!string.IsNullOrEmpty(this.IdentityFile))
            {
                try
                {
                    identity = SQRLIdentity.FromFile(this.IdentityFile);
                }
                catch (Exception ex)
                {

                    await new MessageBoxViewModel(_loc.GetLocalizationValue("ErrorTitleGeneric"),
                        string.Format(_loc.GetLocalizationValue("FileImportErrorMessage"), ex.Message),
                        MessageBoxSize.Medium, MessageBoxButtons.OK, MessageBoxIcons.ERROR)
                        .ShowDialog(this);
                }
            }

            if (identity != null)
            {
                ((MainWindowViewModel)_mainWindow.DataContext).Content = 
                    new ImportIdentitySetupViewModel(identity);
            }   
        }
    }
}
