﻿using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Serilog;
using SQRLCommonUI.AvaloniaExtensions;
using SQRLPlatformAwareInstaller.ViewModels;
using SQRLPlatformAwareInstaller.Views;
using System.Runtime.InteropServices;
using ToolBox.Bridge;

namespace SQRLPlatformAwareInstaller
{
    
    public class App : Application
    {
        private static IBridgeSystem _bridgeSystem { get; set; } = BridgeSystem.Bash;
        private static ShellConfigurator _shell { get; set; } = new ShellConfigurator(_bridgeSystem);
        public override void Initialize()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || 
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (!Utils.IsAdmin())
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        Log.Information("Launched on Linux without Sudo, trying to re-launch if possible");
                        Log.Information("Checking if pkexec exists"); //This allows us to elevate a program
                        var result = _shell.Term("command -v pkexec", Output.Internal);
                        if(string.IsNullOrEmpty(result.stderr) && !string.IsNullOrEmpty(result.stdout))
                        {
                            Log.Information("pkexec exists!");
                            result = _shell.Term("command -v $SQRL_HOME/SQRLPlatformAwareInstaller_linux");
                            if(string.IsNullOrEmpty(result.stderr) && !string.IsNullOrEmpty(result.stdout))
                            {
                                Log.Information("Found Installer in Path!");
                                _shell.Term("pkexec SQRLPlatformAwareInstaller_linux",Output.External);
                                Environment.Exit(0);
                            }
                            else
                                goto NoGo;
                        }
                        else 
                            goto NoGo;
                        
                    }
                    
                    NoGo:
                    throw new System.Exception("This app must be run as an administrator in Windows or sudo/root in Linux");
                    
                }
            }

            AvaloniaXamlLoader.Load(this);

            // This is here only to be able to manually load a specific translation 
            // during development by setting CurrentLocalization it to something 
            // like "en-US" or "de-DE";
            LocalizationExtension loc = new LocalizationExtension();
            LocalizationExtension.CurrentLocalization = LocalizationExtension.DEFAULT_LOC; 
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                    Width = 600,
                    Height = 525
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
