namespace Cantus.Client.ViewModels;

/// <summary>Where the Find a song dialog is in a lookup. Client-only; never sent to the server.</summary>
public enum SongLookupState
{
    Idle,
    SearchingLibrary,
    SearchingOnline,
    Results,
    Empty,
    Error,
    RateLimited
}
