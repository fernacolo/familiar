namespace fam.CommandLine
{
    internal class CommandLineArgument
    {
        public CommandLineArgument( CommandLineParser parser, CommandLineOption option, int argIndex, string argText, int charIndex, string value )
        {
            Parser = parser;
            Option = option;
            ArgIndex = argIndex;
            ArgText = argText;
            CharIndex = charIndex;
            Value = value;
        }

        public CommandLineParser Parser { get; }
        public CommandLineOption Option { get; }
        public int ArgIndex { get; }
        public string ArgText { get; }
        public int CharIndex { get; }
        public string Value { get; }

        public string Nickname
        {
            get
            {
                if ( CharIndex == -1 )
                    return ArgText;
                return "-" + ArgText[CharIndex];
            }
        }
    }
}