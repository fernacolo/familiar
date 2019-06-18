using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using fam.DataFiles;
using fam.Diagnostics;

namespace fam.UI
{
    /// <summary>
    /// Interaction logic for SearchWindow.xaml
    /// </summary>
    public partial class SearchWindow : Window
    {
        // If the user presses key down, delay updates from search. This prevents losing focus/position while
        // the user is navigating to a row in the result.
        private static readonly TimeSpan UpdateDelayForKeyDown = TimeSpan.FromSeconds( 2 );

        private readonly TraceSource _trace;
        private readonly Searcher _searcher;
        private Findings _lastFindings;
        private DateTime _noUpdatesBefore;

        public SearchWindow( Searcher searcher )
        {
            _trace = DiagnosticsCenter.GetTraceSource( nameof( SearchWindow ) );
            _searcher = searcher ?? throw new ArgumentNullException( nameof( searcher ) );
            CurrentFindings = new ObservableCollection<IStoredItem>();
            DataContext = this;
            InitializeComponent();
        }

        public ObservableCollection<IStoredItem> CurrentFindings { get; }

        public IStoredItem SelectedItem { get; private set; }

        private void OnLoaded( object sender, RoutedEventArgs e )
        {
            TbSearch_TextChanged( sender, new TextChangedEventArgs( e.RoutedEvent, UndoAction.None ) );
        }

        private void FindingsChanged()
        {
            var findings = _searcher.GetFindings();
            if ( findings == _lastFindings )
                return;

            var now = DateTime.UtcNow;
            if ( now < _noUpdatesBefore )
            {
                Task.Delay( _noUpdatesBefore - now + TimeSpan.FromMilliseconds( 100 ) ).ContinueWith(
                    task => Dispatcher.Invoke( FindingsChanged )
                );
                return;
            }

            _trace.TraceVerbose( "Detected new findings" );

            CurrentFindings.Clear();
            if ( findings != null )
                foreach ( var item in findings.FoundItems )
                    CurrentFindings.Add( item );

            _lastFindings = findings;
        }

        private void TbSearch_TextChanged( object sender, TextChangedEventArgs e )
        {
            void OnNewFindings()
            {
                Dispatcher.Invoke( FindingsChanged );
            }

            // 1 second in the past to insure immediate update.
            _noUpdatesBefore = DateTime.UtcNow - TimeSpan.FromSeconds( 1 );

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

        private IStoredItem GetSelected()
        {
            var idx = LbSearchResults.SelectedIndex;
            var count = LbSearchResults.Items.Count;
            if ( idx < 0 || idx >= count )
                return null;
            return (IStoredItem) LbSearchResults.Items[idx];
        }

        private void MoveSelected( int move )
        {
            var idx = LbSearchResults.SelectedIndex + move;
            var count = LbSearchResults.Items.Count;
            if ( idx < 0 || idx >= count )
                return;

            LbSearchResults.SelectedIndex = idx;
            LbSearchResults.ScrollIntoView( LbSearchResults.Items[idx] );
            _noUpdatesBefore = DateTime.UtcNow + UpdateDelayForKeyDown;
        }

        private void CloseAndReturnSelection( object sender, RoutedEventArgs e )
        {
            e.Handled = true;
            SelectedItem = GetSelected();
            _searcher.CancelSearch();
            Close();
        }

        private void CloseAndReturnNull( object sender, ExecutedRoutedEventArgs e )
        {
            SelectedItem = null;
            _searcher.CancelSearch();
            Close();
        }
    }
}