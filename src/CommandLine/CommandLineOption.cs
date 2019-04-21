using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable 642

namespace fam.CommandLine
{
    internal sealed class CommandLineOption
    {
        private static readonly Regex LongNameTemplate = new Regex( @"^[_0-9a-z]+(-[_0-9a-z]+)*$", RegexOptions.IgnoreCase | RegexOptions.Singleline );

        public CommandLineOption( CommandLineOptionSpec spec )
        {
            if ( spec.LongName != null )
            {
                if ( spec.LongName.Length < 2 )
                    throw new ArgumentException( "Long name has less than 2 characters: " + spec.LongName );

                if ( !LongNameTemplate.IsMatch( spec.LongName ) )
                    throw new ArgumentException( "Bad long name: " + spec.LongName );
            }

            LongName = Normalize( spec.LongName );

            var ch = spec.ShortName;
            if ( ch != default )
            {
                if ( ch >= '0' && ch <= '9' )
                    ;
                else if ( ch >= 'A' && ch <= 'Z' )
                    ;
                else if ( ch >= 'a' && ch <= 'z' )
                    ;
                else
                    throw new ArgumentException( "Bad short name: " + ch );

                ShortName = Normalize( spec.ShortName );
            }

            HasValue = spec.HasValue;
            Many = spec.Many;
            Help = spec.Help;
            ValueHelp = spec.ValueHelp;
        }


        public string Description => LongName != null ? "--" + LongName : "-" + ShortName;

        public string LongName { get; }
        public string ShortName { get; }
        public bool HasValue { get; }
        public bool Many { get; }
        public string Help { get; }
        public string ValueHelp { get; }

        public static string Normalize( string name )
        {
            return name.ToLowerInvariant();
        }

        public static string Normalize( char name )
        {
            return Normalize( "" + name );
        }

        public CommandLineArgument GetFirst( CommandLineArgument[] parsedArgs )
        {
            var result = GetFirstOrNull( parsedArgs );
            if ( result == null )
                throw new InvalidOperationException( $"Option {Description} was not found in the parsed arguments." );

            return result;
        }

        public CommandLineArgument GetFirstOrNull( CommandLineArgument[] parsedArgs )
        {
            var index = IndexOfFirst( parsedArgs );
            if ( index == -1 )
                return null;

            return parsedArgs[index];
        }

        public CommandLineArgument ExtractOrNull( CommandLineArgument[] parsedArgs )
        {
            var index = IndexOfFirst( parsedArgs );
            if ( index == -1 )
                return null;

            var result = parsedArgs[index];
            parsedArgs[index] = null;
            return result;
        }

        public int IndexOfFirst( CommandLineArgument[] parsedArgs )
        {
            for ( var i = 0; i < parsedArgs.Length; ++i )
                if ( parsedArgs[i]?.Option == this )
                    return i;

            return -1;
        }

        public static void ValidateMutuallyExclusive( CommandLineArgument[] parsedArgs, params CommandLineOption[] options )
        {
            var optionsSet = new HashSet<CommandLineOption>( options );
            var found = (HashSet<CommandLineOption>) null;
            foreach ( var parsedArg in parsedArgs )
                if ( optionsSet.Contains( parsedArg.Option ) )
                {
                    found = found ?? new HashSet<CommandLineOption>();
                    found.Add( parsedArg.Option );
                }

            if ( found == null || found.Count <= 1 )
                return;

            var sb = new StringBuilder();
            sb.Append( "The following flags are mutually exclusive: " );
            var ft = true;
            foreach ( var option in found )
            {
                if ( ft )
                    ft = false;
                else
                    sb.Append( ", " );
                sb.Append( option.GetFirst( parsedArgs ).Nickname );
            }

            throw new InvalidArgumentsException( sb.ToString() );
        }

        public void ValidateThisForbidsOthers( CommandLineArgument[] parsedArgs, params CommandLineOption[] forbidden )
        {
            var item = GetFirstOrNull( parsedArgs );
            if ( item == null )
                return;

            var optionsSet = new HashSet<CommandLineOption>( forbidden );
            var found = (HashSet<CommandLineOption>) null;
            foreach ( var parsedArg in parsedArgs )
                if ( optionsSet.Contains( parsedArg.Option ) )
                {
                    found = found ?? new HashSet<CommandLineOption>();
                    found.Add( parsedArg.Option );
                }

            if ( found == null || found.Count <= 1 )
                return;

            var sb = new StringBuilder();
            sb.Append( $"When {item.Nickname} is specified, the following flags must not cannot be specified: " );
            var ft = true;
            foreach ( var option in found )
            {
                if ( ft )
                    ft = false;
                else
                    sb.Append( ", " );
                sb.Append( option.GetFirst( parsedArgs ).Nickname );
            }

            throw new InvalidArgumentsException( sb.ToString() );
        }

        public static int ComputeFlagsColumnSize( IReadOnlyList<CommandLineOption> options )
        {
            var result = 0;
            foreach ( var option in options )
            {
                var current = option.ComputeFlagsColumnSize();
                if ( current > result )
                    result = current;
            }

            return result;
        }

        public int ComputeFlagsColumnSize()
        {
            return GetFlagsHelp().Length + 1;
        }

        public string GetFlagsHelp( int minLength = 0 )
        {
            string flagsOnly;
            if ( LongName == null )
                flagsOnly = $"-{ShortName}";
            else if ( ShortName == null )
                flagsOnly = $"--{LongName}";
            else
                flagsOnly = $"-{ShortName}, --{LongName}";

            if ( !HasValue )
                return FillWithSpacesIfSmaller( flagsOnly, minLength );

            var valueHelp = ValueHelp ?? "value";
            return FillWithSpacesIfSmaller( $"{flagsOnly} <{valueHelp}>", minLength );
        }

        private static string FillWithSpacesIfSmaller( string input, int minLength )
        {
            if ( input.Length >= minLength )
                return input;

            var sb = new StringBuilder( minLength );
            sb.Append( input );
            sb.Append( ' ', minLength - input.Length );
            return sb.ToString();
        }

        public void Write( string indent, int flagColumnsSize, int lineSize )
        {
            var beforeHelp = $"{indent}{GetFlagsHelp( flagColumnsSize )}";
            if ( (beforeHelp.Length + Help.Length) <= lineSize )
            {
                Terminal.Write( beforeHelp );
                Terminal.WriteLine( Help );
                return;
            }

            var helpLines = WordWrap( Help, lineSize - beforeHelp.Length );
            if ( helpLines == null || helpLines.Count < 2 )
            {
                Terminal.Write( beforeHelp );
                Terminal.WriteLine( Help );
                return;
            }

            Terminal.Write( beforeHelp );
            Terminal.WriteLine( helpLines[0] );

            var beforeHelpIndent = new string( ' ', beforeHelp.Length );

            for ( var i = 1; i < helpLines.Count; ++i )
            {
                Terminal.Write( beforeHelpIndent );
                Terminal.WriteLine( helpLines[i] );
            }
        }

        private static IReadOnlyList<string> WordWrap( string text, int lineSize )
        {
            var result = new List<string>();
            var all = text.Split( ' ' );
            var pos = 0;
            while ( pos < all.Length )
                result.Add( JoinUpToSize( all, ref pos, lineSize ) );
            return result;
        }

        private static string JoinUpToSize( IReadOnlyList<string> words, ref int pos, int lineSize )
        {
            var start = pos;
            var size = words[pos++].Length;
            while ( pos < words.Count )
            {
                var nextSize = size + 1 + words[pos].Length;
                if ( nextSize > lineSize )
                    break;
                size = nextSize;
                ++pos;
            }

            if ( pos == start + 1 )
                return words[start];

            var sb = new StringBuilder( size );
            for ( var i = start; i < pos; ++i )
            {
                if ( i > start )
                    sb.Append( ' ' );
                sb.Append( words[i] );
            }

            return sb.ToString();
        }
    }
}