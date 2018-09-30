using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using wcmd.DataFiles;
using wcmd.Diagnostics;

namespace wcmd
{
    internal class Searcher
    {
        private readonly TraceSource _trace;
        private readonly DataFile _dataFile;
        private readonly Thread _thread;
        private readonly SemaphoreSlim _newRequest;
        private readonly ConcurrentQueue<Request> _requests;
        private readonly int _maxResults = 50;
        private Findings _findings;

        public Searcher( DataFile dataFile )
        {
            _trace = DiagnosticsCenter.GetTraceSource( this );
            _dataFile = dataFile;
            _newRequest = new SemaphoreSlim( 0, int.MaxValue );
            _requests = new ConcurrentQueue<Request>();
            _thread = new Thread( BackgroundThread );
            _thread.Start();
        }

        /// <summary>
        /// Sets the new search term and triggers a search effort in background. This will cause the findings object to eventually change.
        /// </summary>
        /// <remarks>
        /// This method submits a search request to the background thread. If there is an ongoing search, it will be aborted.
        /// The search results are returned by <see cref="GetFindings"/>.
        /// </remarks>
        public void SetSearchText( string searchText )
        {
            Submit( new Request {Type = RequestType.SetSearchText, SearchText = searchText} );
        }

        /// <summary>
        /// If results changed, safely replace the findings object.
        /// </summary>
        private void SetFindings( bool changed, Matcher matcher, IReadOnlyList<Command> foundItems )
        {
            if ( !changed )
                return;

            _trace.TraceInformation( "New findings: {0}, {1} items", matcher.Term, foundItems.Count );
            var findings = new Findings( matcher, foundItems );
            Interlocked.Exchange( ref _findings, findings );
        }

        /// <summary>
        /// Returns an immutable object that contains the current findings. May be return null or an empty findings object.
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
            var lastPage = (CommandPage) null;

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
                        bool changed;

                        if ( currentMatcher != null && newMatcher.Contains( currentMatcher ) )
                        {
                            // The new search text contains the previous one (i.e. previous = abc, new = abcd).
                            // We don't need to start search from zero.

                            // Immediately remove commands that don't match the new search text.
                            var beforeCount = currentResults.Count;
                            for ( var i = currentResults.Count - 1; i >= 0; --i )
                                if ( !newMatcher.IsMatch( currentResults[i] ) )
                                    currentResults.RemoveAt( i );
                            changed = beforeCount > currentResults.Count;
                        }
                        else
                        {
                            // The new search text does not contain the previous one. We need to start from zero.
                            currentResults.Clear();
                            lastPage = null;
                            changed = true;
                        }

                        currentMatcher = newMatcher;

                        // Set findings and resume searching.
                        SetFindings( changed, currentMatcher, currentResults );
                        Submit( new Request {Type = RequestType.ResumeSearch} );
                        break;
                    }

                    case RequestType.ResumeSearch:
                    {
                        Debug.Assert( currentMatcher != null );

                        if ( currentResults.Count >= _maxResults )
                            // We can't produce more results.
                            break;

                        if ( lastPage?.Offset == 0L )
                            // We have iterated over all known commands.
                            break;

                        lastPage = _dataFile.ReadCommandsFromEnd( lastPage, _maxResults - currentResults.Count, TimeSpan.FromMilliseconds( 200 ) );
                        var changed = false;
                        foreach ( var commandRecord in lastPage.Items )
                        {
                            var command = new Command( commandRecord );
                            if ( currentMatcher.IsMatch( command ) )
                            {
                                currentResults.Add( command );
                                changed = true;
                                if ( currentResults.Count >= _maxResults )
                                    break;
                            }
                        }

                        // Set findings and resume searching.
                        SetFindings( changed, currentMatcher, currentResults );
                        Submit( new Request {Type = RequestType.ResumeSearch} );
                        break;
                    }

                    case RequestType.Exit:
                        return;

                    default:
                        throw new InvalidOperationException( $"Unexpected request type: {request.Type}" );
                }
            }
        }

        private enum RequestType
        {
            SetSearchText,
            ResumeSearch,
            Exit
        }

        private class Request
        {
            public RequestType Type;
            public string SearchText { get; set; }
        }
    }
}