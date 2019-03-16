namespace wcmd
{
    public interface IMatcher
    {
        string Term { get; }
        bool IsMatch( Command command );
        bool Contains( IMatcher matcher );
    }
}