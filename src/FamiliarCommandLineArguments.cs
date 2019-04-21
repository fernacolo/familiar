namespace fam
{
    public sealed class FamiliarCommandLineArguments
    {
        public bool Verbose { get; set; }
        public bool ShowHelp { get; set; }
        public string TestFile { get; set; }
        public string Connect { get; set; }
        public bool ShowInfo { get; set; }
        public bool SelectWindow { get; set; }
        public string Database { get; set; }
    }
}