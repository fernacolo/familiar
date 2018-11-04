using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using wcmd.Diagnostics;
using wcmd.Native;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace wcmd.UI
{
    /// <summary>
    /// Interaction logic for AttachSelector.xaml
    /// </summary>
    public partial class AttachSelector : Window
    {
        private readonly TraceSource _trace;

        public AttachSelector()
        {
            _trace = DiagnosticsCenter.GetTraceSource( nameof( AttachSelector ) );
            ProcessItems = new ObservableCollection<ProcessItem>();

            DataContext = this;
            InitializeComponent();
        }

        public ObservableCollection<ProcessItem> ProcessItems { get; }

        public int ParentPid { get; private set; }
        public IntPtr TargetWindow { get; private set; }

        private void Window_Loaded( object sender, RoutedEventArgs e )
        {
            UpdateProcessItems();
        }

        private void UpdateProcessItems()
        {
            var processlist = Process.GetProcesses();

            ProcessItems.Clear();
            foreach ( var process in processlist )
            {
                if ( process.MainWindowHandle == IntPtr.Zero )
                    continue;

                if ( string.Equals( process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase ) )
                    continue;

                _trace.TraceInformation( "Adding process {0}.", process.ProcessName );
                ProcessItems.Add( new ProcessItem( _trace, process ) );
            }
        }

        private void Button_Click( object sender, RoutedEventArgs e )
        {
            var item = LbProcesses.SelectedItem;
            if ( item == null )
            {
                MessageBox.Show( "Please, select an item." );
                return;
            }

            var processItem = (ProcessItem) item;
            ParentPid = processItem.Pid;
            TargetWindow = processItem.Window;

            Close();
        }
    }

    public class ProcessItem
    {
        private readonly TraceSource _trace;
        private readonly Process _process;
        private BitmapImage _bitmap;

        public ProcessItem( TraceSource trace, Process process )
        {
            _trace = trace;
            _process = process;
        }

        public int Pid => _process.Id;

        public IntPtr Window => _process.MainWindowHandle;

        public string ProcessName => _process.ProcessName;

        public BitmapImage ImageSource
        {
            get
            {
                if ( _bitmap == null )
                {
                    _trace.TraceInformation( "Computing tile image for {0} ({1})...", _process.ProcessName, _process.Id );
                    try
                    {
                        _bitmap = PrintWindow( _process.MainWindowHandle );
                    }
                    catch ( Exception ex )
                    {
                        _trace.TraceInformation( "{0}", ex );
                    }
                }

                return _bitmap;
            }

            set => _bitmap = value;
        }

        public static BitmapImage PrintWindow( IntPtr hwnd )
        {
            User32.GetWindowRect( hwnd, out var rc );

            using ( var bmp = new Bitmap( rc.Width, rc.Height, PixelFormat.Format32bppArgb ) )
            {
                bool success;
                var lastError = 0;

                using ( var bmpGfx = Graphics.FromImage( bmp ) )
                {
                    var hdcBitmap = bmpGfx.GetHdc();
                    success = User32.PrintWindow( hwnd, hdcBitmap, 0 );
                    if ( !success )
                        lastError = Marshal.GetLastWin32Error();
                    bmpGfx.ReleaseHdc( hdcBitmap );
                }

                if ( !success )
                    throw new InvalidOperationException( $"PrintWindow API returned false. GetLastError: {lastError:X8}" );

                using ( var memory = new MemoryStream() )
                {
                    bmp.Save( memory, ImageFormat.Png );
                    memory.Position = 0;
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    return bitmapImage;
                }
            }
        }
    }
}