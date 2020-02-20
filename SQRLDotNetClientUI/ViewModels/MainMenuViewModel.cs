﻿using Avalonia;
using Avalonia.Controls;
using ReactiveUI;
using SQRLDotNetClientUI.DB.DBContext;
using SQRLDotNetClientUI.Views;
using SQRLUtilsLib;
using System;
using System.IO;
using System.Linq;
using SQRLDotNetClientUI.DB.Models;
using SQRLDotNetClientUI.Models;

namespace SQRLDotNetClientUI.ViewModels
{
    public class MainMenuViewModel : ViewModelBase
    {
        private IdentityManager _identityManager = IdentityManager.Instance;

        private string _siteUrl = "";
        public string SiteUrl { get => _siteUrl; set => this.RaiseAndSetIfChanged(ref _siteUrl, value); }
        public SQRL sqrlInstance { get; set; }

        private SQRLIdentity _currentIdentity;
        public SQRLIdentity CurrentIdentity 
        { 
            get => _currentIdentity;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentIdentity, value);
                this.CurrentIdentityLoaded = (value != null);
            }
        }

        private bool _currentIdentityLoaded = false;
        public bool CurrentIdentityLoaded { get => _currentIdentityLoaded; set => this.RaiseAndSetIfChanged(ref _currentIdentityLoaded, value); }

        public String _IdentityName = "";
        public String IdentityName { get => _IdentityName; set => this.RaiseAndSetIfChanged(ref _IdentityName, value); }

        public AuthenticationViewModel AuthVM { get; set; }
   
        public MainMenuViewModel(SQRL sqrlInstance)
        {
            this.Title = "SQRL Client";
            this.sqrlInstance = sqrlInstance;

            this.CurrentIdentity = _identityManager.CurrentIdentity;
            this.IdentityName = this.CurrentIdentity?.IdentityName;

            _identityManager.IdentityChanged += OnIdentityChanged;

            string[] commandLine = Environment.CommandLine.Split(" ");
            if(commandLine.Length>1)
            {

               if (Uri.TryCreate(commandLine[1], UriKind.Absolute, out Uri result) && this.CurrentIdentity!=null)
                {
                    AuthenticationViewModel authView = new AuthenticationViewModel(this.sqrlInstance, this.CurrentIdentity, result);
                    AvaloniaLocator.Current.GetService<MainWindow>().Height = 300;
                    AvaloniaLocator.Current.GetService<MainWindow>().Width = 400;
                    AuthVM = authView;
                }
            }
        }

        private void OnIdentityChanged(object sender, IdentityChangedEventArgs e)
        {
            this.IdentityName = e.IdentityName;
            this.CurrentIdentity = _identityManager.CurrentIdentity;
        }

        public MainMenuViewModel()
        {
            this.Title = "SQRL Client";
        }

        public void OnNewIdentityClick()
        {
            ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).Content = new NewIdentityViewModel(this.sqrlInstance);
        }

        public void ExportIdentity()
        {
            ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).Content = new ExportIdentityViewModel(this.sqrlInstance, this.CurrentIdentity);
        }

        public void ImportIdentity()
        {
            ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).Content = new ImportIdentityViewModel(this.sqrlInstance);
        }

        public async void SwitchIdentity()
        {
            SelectIdentityDialogView selectIdentityDialog = new SelectIdentityDialogView();
            selectIdentityDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await selectIdentityDialog.ShowDialog(AvaloniaLocator.Current.GetService<MainWindow>());

            /*
            OpenFileDialog ofd = new OpenFileDialog();
            FileDialogFilter fdf = new FileDialogFilter();
            fdf.Name = "SQRL Identity";
            fdf.Extensions.Add("sqrl");
            ofd.Filters.Add(fdf);
            var file = await ofd.ShowAsync(AvaloniaLocator.Current.GetService<MainWindow>());
            if (file != null && file.Length > 0)
            {
                this.CurrentIdentity = SQRLIdentity.FromFile(file[0]);
                this.CurrentIdentity.IdentityName = Path.GetFileNameWithoutExtension(file[0]);
                this.IdentityName = this.CurrentIdentity.IdentityName;
                UserData result = null;
                using (var db = new SQRLDBContext())
                {
                    result = db.UserData.FirstOrDefault();
                    if (result != null)
                    {
                        result.LastLoadedIdentity = file[0];
                        db.SaveChanges();
                    }
                }
            }
            */
        }

        public void IdentitySettings()
        {
            ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).Content = 
                new IdentitySettingsViewModel(this.sqrlInstance);
        }

        public void Login()
        {
            if(!string.IsNullOrEmpty(this.SiteUrl) && this.CurrentIdentity!=null)
            {
                if (Uri.TryCreate(this.SiteUrl, UriKind.Absolute, out Uri result))
                {
                    AuthenticationViewModel authView = new AuthenticationViewModel(this.sqrlInstance, this.CurrentIdentity, result);
                    AvaloniaLocator.Current.GetService<MainWindow>().Height = 300;
                    AvaloniaLocator.Current.GetService<MainWindow>().Width = 400;
                    AuthVM = authView;
                    ((MainWindowViewModel)AvaloniaLocator.Current.GetService<MainWindow>().DataContext).Content = AuthVM;
                }
            }
        }
    }

}
