using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ReactiveUI;
using SQRLDotNetClientUI.Views;
using SQRLUtilsLib;

namespace SQRLDotNetClientUI.ViewModels
{
    public class ImportIdentityViewModel: ViewModelBase
    {       
        private string _textualIdentity = "";
        private bool _showQrImport = false;
        private Bitmap _cameraFrame = null;

        /// <summary>
        /// Gets or sets a value indicating whether or not to display the
        /// "import qr-code" UI.
        /// </summary>
        public bool ShowQrImport
        {
            get { return _showQrImport; }
            set 
            { 
                this.RaiseAndSetIfChanged(ref _showQrImport, value);
                if (value) ImportQrCode();
            }
        }

        /// <summary>
        /// Gets or sets the current frame from the webcam.
        /// </summary>
        Bitmap CameraFrame
        {
            get { return _cameraFrame; }
            set { this.RaiseAndSetIfChanged(ref _cameraFrame, value); }
        }

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

        /// <summary>
        /// Toggles the visibility of the qr code UI.
        /// </summary>
        /// <param name="visible">Set to <c>true</c> to show the qr code, 
        /// or <c>false</c> to hide it</param>
        public void ToggleQrImport(bool visible)
        {
            this.ShowQrImport = visible;
        }

        public async void ImportQrCode()
        {

            await Task.Run(() =>
            {
                VideoCapture capture = new VideoCapture();
                capture.Open(0, VideoCaptureAPIs.ANY);

                if (!capture.IsOpened())
                {
                    // TODO: Error!
                }

                for (int i = 0; i < 1000; i++)
                {
                    using (var frameMat = capture.RetrieveMat())
                    {
                        if (frameMat.Empty()) continue;

                        var stream = new MemoryStream();
                        BitmapConverter.ToBitmap(frameMat).Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                        stream.Seek(0, SeekOrigin.Begin);
                        Bitmap frame = new Bitmap(stream);

                        Dispatcher.UIThread.Post(() =>
                            this.CameraFrame = frame
                        );
                    }
                }

                capture.Dispose();
            });
            
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
