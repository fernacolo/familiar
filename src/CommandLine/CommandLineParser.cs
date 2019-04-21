using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace fam.CommandLine
{
    internal sealed class CommandLineParser
    {
        private readonly Dictionary<string, CommandLineOption> _options;

        public CommandLineParser( IReadOnlyList<CommandLineOption> options )
        {
            _options = new Dictionary<string, CommandLineOption>();
            for ( var i = 0; i < options.Count; ++i )
            {
                var option = options[i];
                ValidateAndAddOption( option, i );
            }
        }

        private void ValidateAndAddOption( CommandLineOption option, int i )
        {
            if ( option.LongName != null )
            {
                Debug.Assert( !string.IsNullOrWhiteSpace( option.LongName ) );
                if ( _options.TryGetValue( option.LongName, out _ ) )
                    throw new ArgumentException( $"INTERNAL ERROR: options[{i}] is {option.LongName}, which was specified before." );
                _options[option.LongName] = option;
            }

            if ( option.ShortName != null )
            {
                Debug.Assert( !string.IsNullOrWhiteSpace( option.ShortName ) );
                if ( _options.TryGetValue( option.ShortName, out _ ) )
                    throw new ArgumentException( $"INTERNAL ERROR: options[{i}] is {option.ShortName}, which was specified before." );
                _options[option.ShortName] = option;
            }
        }

        public CommandLineArgument[] Parse( string[] args )
        {
            if ( args.Length < 1 )
                throw new ArgumentException( "The args[0] must contain the initiator command." );

            var result = new List<CommandLineArgument>();

            var argsScanner = new ArgumentScanner( args );
            argsScanner.MoveNext();

            while ( argsScanner.MoveNext() )
            {
                var curArg = argsScanner.Current;

                if ( curArg == null )
                    throw new ArgumentNullException( $"{nameof( args )}[{argsScanner.Index}]" );

                if ( curArg.StartsWith( "--" ) )
                {
                    ParseOptionArg( curArg, argsScanner, result );
                    continue;
                }

                if ( curArg.StartsWith( "-" ) )
                {
                    ParseMultiOptionsArg( curArg, argsScanner, result );
                    continue;
                }

                throw new InvalidArgumentsException( $"Unrecognized argument at position {argsScanner.Index}: \"{curArg}\"." );
            }

            for ( var i = 0; i < result.Count; ++i )
            {
                var opi = result[i];
                if ( !opi.Option.Many )
                    continue;

                for ( var j = i + 1; j < result.Count; ++j )
                {
                    var opj = result[j];
                    if ( opj.Option == opi.Option )
                        throw new InvalidArgumentsException( $"Flag {opi.Nickname} can be specified only once, but was found at positions {opi.ArgIndex} and {opj.ArgIndex}." );
                }
            }

            return result.ToArray();
        }

        private void ParseOptionArg( string curArg, ArgumentScanner argsScanner, List<CommandLineArgument> result )
        {
            var option = GetOptionOrNull( curArg.Substring( 2 ) );
            if ( option == null )
                throw new InvalidArgumentsException( $"Unrecognized argument at position {argsScanner.Index}: \"{curArg}\"." );

            if ( !option.HasValue )
            {
                AddNamedOption( result, option, argsScanner.Index, curArg, null );
                return;
            }

            if ( !argsScanner.HasNext || argsScanner.Next.StartsWith( "-" ) )
                throw new InvalidArgumentsException( $"Error in argument at position {argsScanner.Index}: flag {curArg} requires a value." );

            argsScanner.MoveNext();
            AddNamedOption( result, option, argsScanner.Index - 1, curArg, argsScanner.Current );
        }

        private void ParseMultiOptionsArg( string curArg, ArgumentScanner argsScanner, List<CommandLineArgument> result )
        {
            for ( var i = 1; i < curArg.Length; ++i )
            {
                var ch = curArg[i];
                var option = GetOptionOrNull( ch );
                if ( option == null )
                    throw new InvalidArgumentsException( $"Argument at position {argsScanner.Index} contains unrecognized character: {DecorateCharacter( curArg, i )}." );

                if ( !option.HasValue )
                {
                    AddCharOption( result, option, argsScanner.Index, curArg, i, null );
                    continue;
                }

                if ( i != curArg.Length - 1 || !argsScanner.HasNext || argsScanner.Next.StartsWith( "-" ) )
                    throw new InvalidArgumentsException( $"Error in argument at position {argsScanner.Index}: flag -{curArg[i]} requires a value." );

                argsScanner.MoveNext();
                AddCharOption( result, option, argsScanner.Index - 1, curArg, i, argsScanner.Current );
            }
        }

        private static string DecorateCharacter( string arg, int charIndex )
        {
            return arg.Substring( 0, charIndex ) + "[" + arg[charIndex] + "]" + arg.Substring( charIndex + 1 );
        }

        private void AddNamedOption( ICollection<CommandLineArgument> result, CommandLineOption option, int argIndex, string arg, string value )
        {
            result.Add( new CommandLineArgument( this, option, argIndex, arg, -1, value ) );
        }

        private void AddCharOption( ICollection<CommandLineArgument> result, CommandLineOption option, int argIndex, string arg, int charIndex, string value )
        {
            result.Add( new CommandLineArgument( this, option, argIndex, arg, charIndex, value ) );
        }

        private CommandLineOption GetOptionOrNull( string name )
        {
            name = CommandLineOption.Normalize( name );
            if ( !_options.TryGetValue( name, out var result ) )
                return null;
            return result;
        }

        private CommandLineOption GetOptionOrNull( char ch )
        {
            var name = CommandLineOption.Normalize( ch );
            if ( !_options.TryGetValue( name, out var result ) )
                return null;
            return result;
        }
    }
}