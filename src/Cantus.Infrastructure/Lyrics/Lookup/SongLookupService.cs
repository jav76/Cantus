using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cantus.Infrastructure.Lyrics.Lookup;

/// <summary>
/// Song lookup by remembered lyrics (scoped).
/// <list type="number">
/// <item>Local: scan the <see cref="LyricsSearchIndex"/> snapshot and decide with
/// <see cref="SongGuessRanker"/>. A scan cut short by its time budget never accepts a local song.</item>
/// <item>Online, for scope Auto when local is not accepted, or scope Online: NetEase
/// first, then LRCLIB only while the merged outcome is still not accepted. At most
/// <see cref="MAX_PROVIDER_REQUESTS_PER_LOOKUP"/> provider requests within
/// <see cref="EXTERNAL_DEADLINE_SECONDS"/>, one spend of the caller's
/// <see cref="SongLookupBudget"/>, and answers reused from <see cref="SongLookupQueryCache"/>.</item>
/// <item>Provider hits are scored by the same scorer with the snapshot's term table,
/// merged with the local candidates and decided again.</item>
/// </list>
/// It has no lyrics cache repository, so a lookup cannot write <c>CachedLyrics</c>
/// or the negative cache. The fragment and lyric text are never logged.
/// </summary>
public sealed class SongLookupService : ISongLookupService
{
    public const int EXTERNAL_DEADLINE_SECONDS = 10;
    public const int MAX_PROVIDER_REQUESTS_PER_LOOKUP = 3;

    private const int NETEASE_REQUESTS_PER_SEARCH = 1;

    /// <summary>
    /// Fragment providers in search order: LRCLIB is the now-playing primary, so its budget goes last.
    /// </summary>
    private static readonly string[] ProviderOrder =
    [
        NeteaseLyricsProvider.PROVIDER_NAME,
        LrclibLyricsProvider.PROVIDER_NAME,
    ];

    private static readonly TimeSpan ExternalDeadline = TimeSpan.FromSeconds(EXTERNAL_DEADLINE_SECONDS);

    private readonly ILyricsCorpusReader _corpusReader;
    private readonly LyricsSearchIndex _index;
    private readonly IReadOnlyList<ILyricsFetchProvider> _providers;
    private readonly ProviderRateGate _rateGate;
    private readonly SongLookupQueryCache _queryCache;
    private readonly SongLookupBudget _budget;
    private readonly IOptions<SongLookupOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SongLookupService> _logger;

    /// <summary>Time budget of the local scan; tests shorten it to force a truncated scan.</summary>
    internal TimeSpan LocalScanBudget { get; init; } = LyricsSearchIndex.ScanBudget;

    public SongLookupService(
        ILyricsCorpusReader corpusReader,
        LyricsSearchIndex index,
        IReadOnlyList<ILyricsFetchProvider> providers,
        ProviderRateGate rateGate,
        SongLookupQueryCache queryCache,
        SongLookupBudget budget,
        IOptions<SongLookupOptions> options,
        TimeProvider timeProvider,
        ILogger<SongLookupService> logger)
    {
        _corpusReader = corpusReader;
        _index = index;
        _providers = providers;
        _rateGate = rateGate;
        _queryCache = queryCache;
        _budget = budget;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SongLookupResult> LookupAsync(
        string fragment,
        SongLookupScope scope,
        string spotifyUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(spotifyUserId);
        LyricFragment prepared = LyricTextNormalizer.PrepareFragment(fragment);
        if (!prepared.IsValid)
        {
            throw new ArgumentException($"The fragment cannot be searched ({prepared.Status}).", nameof(fragment));
        }

        long started = _timeProvider.GetTimestamp();
        LyricsIndexSnapshot snapshot = await _index.GetSnapshotAsync(_corpusReader, cancellationToken);
        LyricsScanResult scan = _index.Scan(snapshot, prepared, LocalScanBudget, cancellationToken);

        LookupRun run = new(prepared, snapshot, scan, ResolveFragmentProviders());
        run.Decide();

        bool searchOnline = scope == SongLookupScope.Online
            || (scope == SongLookupScope.Auto && run.Outcome != SongMatchOutcome.Accepted);
        if (searchOnline)
        {
            await SearchOnlineAsync(run, fragment, spotifyUserId, cancellationToken);
        }

        bool onlineRecommended = scope == SongLookupScope.Local
            && run.Outcome != SongMatchOutcome.Accepted
            && run.Providers.Any(p => p.Provider is not null)
            && _budget.GetRetryAfter(spotifyUserId) == TimeSpan.Zero;

        SongLookupResult result = new(
            run.Outcome,
            onlineRecommended,
            run.Ranking.Guesses.Select(ToGuess).ToList(),
            run.Providers.Select(p => p.ToStatus()).ToList());

        _logger.LogInformation(
            "Song lookup ({Scope}): {TokenCount} tokens, corpus {CorpusSize} songs, top {TopScore:F3}, "
                + "margin {Margin:F3}, outcome {Outcome}, providers [{ProviderStatuses}] in {ElapsedMs} ms",
            scope,
            prepared.Tokens.Count,
            scan.CorpusSize,
            run.Ranking.TopScore,
            run.Ranking.Margin,
            run.Outcome,
            string.Join(", ", run.Providers.Select(p => $"{p.Name}={p.State}")),
            (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds);

        return result;
    }

    private List<ProviderSlot> ResolveFragmentProviders()
    {
        SongLookupOptions options = _options.Value;
        List<ILyricsFragmentSearchProvider> available = _providers.OfType<ILyricsFragmentSearchProvider>().ToList();

        return ProviderOrder
            .Select(name =>
            {
                bool enabled = options.ExternalSearchEnabled
                    && (name != LrclibLyricsProvider.PROVIDER_NAME || options.LrclibTitleGuessEnabled);
                ILyricsFragmentSearchProvider? provider = enabled
                    ? available.FirstOrDefault(p => string.Equals(p.ProviderName, name, StringComparison.Ordinal))
                    : null;
                return new ProviderSlot(name, provider);
            })
            .ToList();
    }

    private async Task SearchOnlineAsync(
        LookupRun run,
        string fragment,
        string spotifyUserId,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = new(ExternalDeadline, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token);

        string cacheKey = run.Fragment.ToSearchText();
        bool budgetSpent = false;
        int requestsSent = 0;

        for (int i = 0; i < run.Providers.Count; i++)
        {
            ProviderSlot slot = run.Providers[i];
            if (slot.Provider is null)
            {
                continue;
            }

            // Scope Online always asks the first provider; later ones only while
            // the merged outcome is still not accepted, whatever became of the first.
            if (i > 0 && run.Outcome == SongMatchOutcome.Accepted)
            {
                continue;
            }

            if (_queryCache.TryGet(slot.Name, cacheKey, out IReadOnlyList<LyricsFragmentHit> cached))
            {
                slot.State = SongLookupProviderState.Ok;
                run.AddHits(slot.Name, cached);
                continue;
            }

            if (deadline.IsCancellationRequested)
            {
                slot.State = SongLookupProviderState.Unavailable;
                continue;
            }

            int permits = _rateGate.GetAvailableLookupPermits(slot.Name);
            if (permits <= 0)
            {
                slot.MarkRateLimited(_rateGate.GetRetryAfter(slot.Name, isLookup: true));
                continue;
            }

            IReadOnlyList<string> titleGuesses = [];
            int requestCost = NETEASE_REQUESTS_PER_SEARCH;

            // An answer from fewer title guesses than a full search would send is
            // incomplete, so it is not cached as the provider's answer.
            bool cacheable = true;
            if (slot.Name == LrclibLyricsProvider.PROVIDER_NAME)
            {
                int allowed = Math.Min(
                    LrclibLyricsProvider.MAX_TITLE_GUESSES,
                    MAX_PROVIDER_REQUESTS_PER_LOOKUP - requestsSent);
                IReadOnlyList<string> fullGuesses = TitleGuessGenerator.Generate(
                    run.Fragment.Tokens,
                    allowed,
                    run.Snapshot.Terms);
                titleGuesses = fullGuesses.Take(permits).ToList();
                requestCost = titleGuesses.Count;
                cacheable = titleGuesses.Count == fullGuesses.Count;
            }

            if (requestCost == 0 || requestsSent + requestCost > MAX_PROVIDER_REQUESTS_PER_LOOKUP)
            {
                continue;
            }

            if (!budgetSpent)
            {
                if (!_budget.TryAcquire(spotifyUserId, out TimeSpan budgetRetryAfter))
                {
                    slot.MarkRateLimited(budgetRetryAfter);
                    continue;
                }

                budgetSpent = true;
            }

            requestsSent += requestCost;
            IReadOnlyList<LyricsFragmentHit>? hits = await SearchProviderAsync(
                slot,
                new LyricsFragmentQuery(fragment.Trim(), titleGuesses),
                linked.Token,
                cancellationToken);

            if (hits is not null)
            {
                slot.State = SongLookupProviderState.Ok;
                if (cacheable)
                {
                    _queryCache.Store(slot.Name, cacheKey, hits);
                }

                run.AddHits(slot.Name, hits);
            }
        }
    }

    /// <summary>The provider's hits, or null after marking the slot unavailable or rate limited.</summary>
    private async Task<IReadOnlyList<LyricsFragmentHit>?> SearchProviderAsync(
        ProviderSlot slot,
        LyricsFragmentQuery query,
        CancellationToken linkedToken,
        CancellationToken callerToken)
    {
        // Exceptions are logged by type only: their messages can carry the
        // request URL, and with it the fragment.
        try
        {
            return await slot.Provider!.SearchByFragmentAsync(query, linkedToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderRateLimitedException ex)
        {
            slot.MarkRateLimited(ex.RetryAfter);
            _logger.LogDebug(
                "{Provider} fragment search is rate limited for {RetryAfterSeconds:F0} s",
                slot.Name,
                ex.RetryAfter.TotalSeconds);
        }
        catch (ProviderPayloadRejectedException ex)
        {
            TimeSpan cooldown = _rateGate.ReportLookupCooldown(slot.Name);
            slot.State = SongLookupProviderState.Unavailable;
            _logger.LogWarning(
                "{Provider} rejected a fragment search (payload code {PayloadCode}); "
                    + "pausing lookups for {CooldownSeconds:F0} s",
                slot.Name,
                ex.PayloadCode,
                cooldown.TotalSeconds);
        }
        catch (Exception ex)
        {
            slot.State = SongLookupProviderState.Unavailable;
            _logger.LogWarning(
                "{Provider} fragment search failed with {ExceptionType}",
                slot.Name,
                ex.GetType().Name);
        }

        return null;
    }

    private static SongGuess ToGuess(SongGuessGroup group)
    {
        SongCandidate head = group.Head;
        return new SongGuess(
            head.SourceName,
            head.SpotifyTrackId,
            head.Title,
            head.Artist,
            head.Album,
            head.DurationMs,
            head.Score,
            head.Verified,
            head.MatchedLines,
            group.VersionCount);
    }

    private sealed class ProviderSlot
    {
        public ProviderSlot(string name, ILyricsFragmentSearchProvider? provider)
        {
            Name = name;
            Provider = provider;
            State = provider is null ? SongLookupProviderState.Disabled : SongLookupProviderState.Skipped;
        }

        public string Name { get; }

        public ILyricsFragmentSearchProvider? Provider { get; }

        public SongLookupProviderState State { get; set; }

        public TimeSpan? RetryAfter { get; private set; }

        public void MarkRateLimited(TimeSpan retryAfter)
        {
            State = SongLookupProviderState.RateLimited;
            RetryAfter = retryAfter;
        }

        public SongLookupProviderStatus ToStatus() =>
            new(Name, State, State == SongLookupProviderState.RateLimited ? RetryAfter : null);
    }

    /// <summary>The candidates gathered so far and the decision taken on them.</summary>
    private sealed class LookupRun
    {
        private readonly List<SongCandidate> _candidates;
        private readonly bool _truncated;
        private LyricFragmentScorer? _externalScorer;

        public LookupRun(
            LyricFragment fragment,
            LyricsIndexSnapshot snapshot,
            LyricsScanResult scan,
            List<ProviderSlot> providers)
        {
            Fragment = fragment;
            Snapshot = snapshot;
            Providers = providers;
            _candidates = scan.Candidates.ToList();
            _truncated = scan.Truncated;
            Ranking = new SongGuessRanking(SongMatchOutcome.None, 0, 0, []);
        }

        public LyricFragment Fragment { get; }

        public LyricsIndexSnapshot Snapshot { get; }

        public List<ProviderSlot> Providers { get; }

        public SongGuessRanking Ranking { get; private set; }

        public SongMatchOutcome Outcome { get; private set; } = SongMatchOutcome.None;

        public void Decide()
        {
            Ranking = SongGuessRanker.Rank(_candidates);

            // A scan cut short may have missed a closer local song, so it never
            // accepts a local head. A decisive provider hit is still accepted.
            bool localHead = Ranking.Guesses.Count > 0 && Ranking.Guesses[0].Head.Origin == SongCandidateOrigin.Local;
            Outcome = _truncated && localHead && Ranking.Outcome == SongMatchOutcome.Accepted
                ? SongMatchOutcome.Ambiguous
                : Ranking.Outcome;
        }

        public void AddHits(string providerName, IReadOnlyList<LyricsFragmentHit> hits)
        {
            // Lyric-text search hits the scorer cannot confirm (for example Traditional
            // against Simplified Chinese) are still shown, unverified, from the top few.
            bool keepUnverified = providerName == NeteaseLyricsProvider.PROVIDER_NAME;
            _externalScorer ??= new LyricFragmentScorer(Fragment, Snapshot.Terms);

            for (int i = 0; i < hits.Count; i++)
            {
                LyricsFragmentHit hit = hits[i];
                LyricTextMatch match = _externalScorer.ScoreText(hit.SearchableText);
                bool verified = match.Match.Score >= LyricMatchConstants.RESULT_MIN_SCORE;
                if (!verified && (!keepUnverified || i >= LyricMatchConstants.MAX_UNVERIFIED_GUESSES))
                {
                    continue;
                }

                LyricsCandidate song = hit.Candidate;
                _candidates.Add(new SongCandidate(
                    SongCandidateOrigin.External,
                    providerName,
                    null,
                    song.Title,
                    song.Artist,
                    song.Album,
                    song.Duration > TimeSpan.Zero ? (int)song.Duration.TotalMilliseconds : null,
                    match.Match.Score,
                    verified,
                    match.MatchedLines,
                    match.Tokens));
            }

            Decide();
        }
    }
}
