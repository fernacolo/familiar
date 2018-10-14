using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using wcmd.Diagnostics;

namespace wcmd.UI
{
    /// <summary>
    /// Interaction logic for SearchWindow.xaml
    /// </summary>
    public partial class SearchWindow : Window
    {
        private readonly TraceSource _trace;
        private readonly Searcher _searcher;
        private Findings _lastFindings;

        public SearchWindow( Searcher searcher )
        {
            _trace = DiagnosticsCenter.GetTraceSource( this );
            _searcher = searcher ?? throw new ArgumentNullException( nameof( searcher ) );
            InitializeComponent();
        }

        public string SelectedCommand { get; private set; }

        private void FindingsChanged()
        {
            var findings = _searcher.GetFindings();
            if ( findings == _lastFindings )
                return;

            _trace.TraceInformation( "Detected new findings" );
            LbSearchResults.Items.Clear();
            if ( findings != null )
                foreach ( var item in findings.FoundItems )
                    LbSearchResults.Items.Add( new ListBoxItem {Content = item.Original, Height = 20} );

            _lastFindings = findings;
        }

        private void TbSearch_TextChanged( object sender, TextChangedEventArgs e )
        {
            void OnNewFindings()
            {
                Dispatcher.Invoke( FindingsChanged );
            }

            _searcher.SetSearchText( TbSearch.Text, OnNewFindings );
        }

        private void TbSearch_PreviewKeyDown( object sender, KeyEventArgs e )
        {
            switch ( e.Key )
            {
                case Key.Up:
                    MoveSelected( -1 );
                    e.Handled = true;
                    return;

                case Key.Down:
                    MoveSelected( 1 );
                    e.Handled = true;
                    return;

                case Key.Enter:
                    CloseAndReturnSelection( sender, e );
                    return;
            }
        }

        private void LbSearchResults_KeyDown( object sender, KeyEventArgs e )
        {
            if ( e.Key == Key.Enter )
                CloseAndReturnSelection( sender, e );
        }

        private void LbSearchResults_MouseDoubleClick( object sender, InputEventArgs e )
        {
            CloseAndReturnSelection( sender, e );
        }

        private string GetSelected()
        {
            var idx = LbSearchResults.SelectedIndex;
            var count = LbSearchResults.Items.Count;
            if ( idx < 0 || idx >= count )
                return null;
            return (string) ((ListBoxItem) LbSearchResults.Items[idx]).Content;
        }

        private void MoveSelected( int move )
        {
            var idx = LbSearchResults.SelectedIndex + move;
            var count = LbSearchResults.Items.Count;
            if ( idx < 0 || idx >= count )
                return;

            LbSearchResults.SelectedIndex = idx;
            LbSearchResults.ScrollIntoView( LbSearchResults.Items[idx] );
        }

        private void CloseAndReturnSelection( object sender, RoutedEventArgs e )
        {
            SelectedCommand = GetSelected();
            _searcher.CancelSearch();
            Close();
        }

        private void CloseAndReturnNull( object sender, ExecutedRoutedEventArgs e )
        {
            SelectedCommand = null;
            _searcher.CancelSearch();
            Close();
        }
    }
}