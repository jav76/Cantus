using Cantus.Core.Models;

namespace Cantus.Core.Interfaces;

public interface IPlaybackInterpolator
{
    TimeSpan CalculateCurrentPosition(PlaybackState state, TimeSpan userOffset);
    void Reset();
}
