using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using wcmd.DataFiles;
using wcmd.Diagnostics;

namespace wcmd
{
    public sealed class Searcher
    {
        private readonly TraceSource _trace;
        private readonly IDataFile _dataFile;
        private readonly Thread _thread;
        private readonly SemaphoreSlim _newRequest;
        private readonly ConcurrentQueue<Request> _requests;
        private readonly int _maxResults = 50;
        private Findings _findings;

        public Searcher( IDataFile dataFile )
        {
            _trace = DiagnosticsCenter.GetTraceSource( this );
            _dataFile = dataFile;
            _newRequest = new SemaphoreSlim( 0, int.MaxValue );
            _requests = new ConcurrentQueue<Request>();
            _thread = new Thread( BackgroundThread );
            _thread.IsBackground = true;
            _thread.Start();
        }

        /// <summary>
        /// Sets the new search term and triggers a search effort in background. This will cause the findings object to eventually change.
        /// </summary>
        /// <remarks>
        /// This method submits a search request to the background thread. If there is an ongoing search, it will be aborted.
        /// The search results are returned by <see cref="GetFindings"/>.
        /// Whenever search results change, the <see cref="onNewFindings"/> action is invoked, if not null. The action is invoked at an
        /// arbitrary thread. Callers must take required precations to update the UI.
        /// </remarks>
        public void SetSearchText( string searchText, Action onNewFindings )
        {
            Submit( new Request {Type = RequestType.SetSearchText, SearchText = searchText, Callback = onNewFindings} );
        }

        /// <summary>
        /// Cancels an ongoing search. This method will make the searcher stop calling the callback specified at <see cref="SetSearchText"/>.
        /// Due to memory ordering artifacts, the callback can still be called a few times after this method is called.
        /// </summary>
        public void CancelSearch()
        {
            Submit( new Request {Type = RequestType.CancelSearch} );
        }

        /// <summary>
        /// Returns an immutable object that contains the current findings. May return null or an empty findings object.
        /// Since the search runs in background, multiple calls may return different objects. This method always returns
        /// immediately.
        /// </summary>
        public Findings GetFindings()
        {
            return Interlocked.CompareExchange( ref _findings, null, null );
        }

        private void Submit( Request request )
        {
            _trace.TraceInformation( "Submitting request: {0}, {1}", request.Type, request.SearchText );
            _requests.Enqueue( request );
            _newRequest.Release();
        }

        private void BackgroundThread()
        {
            var currentMatcher = (Matcher) null;
            var currentResults = new List<Command>();
            var currentCommands = new HashSet<string>();
            var lastRead = _dataFile.Eof;
            var searchCallback = (Action) null;

            for ( ;; )
            {
                _trace.TraceInformation( "Awaiting for request..." );

                _newRequest.Wait();

                var signaled = _requests.TryDequeue( out var request );
                Debug.Assert( signaled );

                _trace.TraceInformation( "Got request: {0}, {1}", request.Type, request.SearchText );

                switch ( request.Type )
                {
                    case RequestType.SetSearchText:
                    {
                        var newMatcher = new Matcher( request.SearchText );
                        searchCallback = request.Callback;
                        bool changed;

                        if ( currentMatcher != null && newMatcher.Contains( currentMatcher ) )
                        {
                            // The new search text contains the previous one (i.e. previous = abc, new = abcd).
                            // We don't need to start search from zero.

                            // Immediately remove commands that don't match the new search text.
                            var beforeCount = currentResults.Count;
                            for ( var i = currentResults.Count - 1; i >= 0; --i )
                            {
                                var currentResult = currentResults[i];
                                if ( !newMatcher.IsMatch( currentResult ) )
                                {
                                    currentResults.RemoveAt( i );
                                    currentCommands.Remove( currentResult.Original );
                                }
                            }

                            changed = beforeCount > currentResults.Count;
                        }
                        else
                        {
                            // The new search text does not contain the previous one. We need to start from zero.
                            currentResults.Clear();
                            currentCommands.Clear();
                            lastRead = _dataFile.Eof;
                            changed = true;
                        }

                        currentMatcher = newMatcher;

                        CompleteSearch( changed, currentMatcher, currentResults, searchCallback );
                        break;
                    }

                    case RequestType.ResumeSearch:
                    {
                        Debug.Assert( currentMatcher != null );

                        if ( currentResults.Count >= _maxResults )
                            // We can't produce more results.
                            break;

                        if ( lastRead == _dataFile.Bof )
                            // We've already read through entire file.
                            break;

                        var changed = false;
                        var sw = Stopwatch.StartNew();
                        do
                        {
                            lastRead = _dataFile.GetPrevious( lastRead );
                            if ( lastRead == _dataFile.Bof )
                                break;

                            var command = new Command( lastRead );
                            if ( currentMatcher.IsMatch( command ) && !currentCommands.Contains( command.Original ) )
                            {
                                currentResults.Add( command );
                                currentCommands.Add( command.Original );
                                changed = true;
                                if ( currentResults.Count >= _maxResults )
                                    break;
                            }
                        } while ( sw.ElapsedMilliseconds <= 200 );

                        CompleteSearch( changed, currentMatcher, currentResults, searchCallback );
                        break;
                    }

                    case RequestType.CancelSearch:
                        searchCallback = null;
                        break;

                    case RequestType.Exit:
                        return;

                    default:
                        throw new InvalidOperationException( $"Unexpected request type: {request.Type}" );
                }
            }
        }

        private void CompleteSearch( bool changed, Matcher matcher, List<Command> foundItems, Action notifyAction )
        {
            if ( changed )
            {
                _trace.TraceInformation( "New findings: {0}, {1} items", matcher.Term, foundItems.Count );
                var items = new List<IStoredCommand>( foundItems.Count );
                foreach ( var command in foundItems )
                    items.Add( command.Stored );

                var findings = new Findings( matcher, items );
                Interlocked.Exchange( ref _findings, findings );
                notifyAction?.Invoke();
            }

            Submit( new Request {Type = RequestType.ResumeSearch} );
        }

        private enum RequestType
        {
            SetSearchText,
            ResumeSearch,
            CancelSearch,
            Exit
        }

        private class Request
        {
            public RequestType Type;
            public string SearchText;
            public Action Callback;
        }
    }
}