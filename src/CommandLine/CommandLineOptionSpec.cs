namespace fam.CommandLine
{
    internal struct CommandLineOptionSpec
    {
        public string LongName;
        public char ShortName;
        public bool HasValue;
#pragma warning disable 649
        public bool Many;
#pragma warning restore 649
        public string Help;
        public string ValueHelp;
    }
}