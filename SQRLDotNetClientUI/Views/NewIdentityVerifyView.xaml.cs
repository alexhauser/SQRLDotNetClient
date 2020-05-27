﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SQRLCommon.AvaloniaExtensions;

namespace SQRLDotNetClientUI.Views
{
    public class NewIdentityVerifyView : UserControl
    {
        public NewIdentityVerifyView()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // This is only here because for some reason the XAML behaviour "FocusOnAttached"
            // isn't working for this window. No clue why, probably a dumb mistake on my part.
            // If anyone gets this working in XAML, this ugly hack can be removed!
            this.FindControl<CopyPasteTextBox>("txtRescueCode").AttachedToVisualTree +=
                (sender, e) => (sender as CopyPasteTextBox).Focus();
        }
    }
}
