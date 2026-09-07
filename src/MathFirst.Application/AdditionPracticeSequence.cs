namespace MathFirst.Application;

using MathFirst.Domain;

public sealed class AdditionPracticeSequence
{
    private readonly IReadOnlyList<AdditionFact> _catalog;
    private readonly Random _random;
    private readonly List<AdditionFact> _currentDeck = [];
    private readonly List<(AdditionFact Fact, int DueAfterSteps)> _recurrenceQueue = [];
    private AdditionFact? _lastYieldedFact;
    private int _deckIndex;

    public const int DefaultRecurrenceDelay = 3;

    public AdditionPracticeSequence(IReadOnlyList<AdditionFact>? catalog = null, int? seed = null)
    {
        _catalog = catalog is { Count: > 0 } ? catalog : AdditionCatalog.CreateFullCatalog();
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        RefillAndShuffleDeck();
    }

    public AdditionPracticeSequence(IReadOnlyList<AdditionFact> catalog, Random random)
    {
        _catalog = catalog is { Count: > 0 } ? catalog : AdditionCatalog.CreateFullCatalog();
        _random = random;
        RefillAndShuffleDeck();
    }

    public int CatalogCount => _catalog.Count;
    public int PendingRecurrenceCount => _recurrenceQueue.Count;

    public AdditionFact GetNextFact()
    {
        // Check if any recurrence is due (DueAfterSteps <= 0)
        for (var i = 0; i < _recurrenceQueue.Count; i++)
        {
            if (_recurrenceQueue[i].DueAfterSteps <= 0)
            {
                var dueFact = _recurrenceQueue[i].Fact;
                _recurrenceQueue.RemoveAt(i);
                _lastYieldedFact = dueFact;
                DecrementPendingRecurrences();
                return dueFact;
            }
        }

        if (_deckIndex >= _currentDeck.Count)
        {
            RefillAndShuffleDeck();
        }

        var nextFact = _currentDeck[_deckIndex++];
        _lastYieldedFact = nextFact;
        DecrementPendingRecurrences();
        return nextFact;
    }

    public void ScheduleRecurrence(AdditionFact fact, int delay = DefaultRecurrenceDelay)
    {
        _recurrenceQueue.Add((fact, delay));
    }

    private void DecrementPendingRecurrences()
    {
        for (var i = 0; i < _recurrenceQueue.Count; i++)
        {
            _recurrenceQueue[i] = (_recurrenceQueue[i].Fact, _recurrenceQueue[i].DueAfterSteps - 1);
        }
    }

    private void RefillAndShuffleDeck()
    {
        _currentDeck.Clear();
        _currentDeck.AddRange(_catalog);

        // Fisher-Yates shuffle
        for (var i = _currentDeck.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_currentDeck[i], _currentDeck[j]) = (_currentDeck[j], _currentDeck[i]);
        }

        // Prevent immediate consecutive duplicate across cycles if deck has more than 1 item
        if (_lastYieldedFact is not null && _currentDeck.Count > 1 && _currentDeck[0] == _lastYieldedFact)
        {
            var swapIndex = _random.Next(1, _currentDeck.Count);
            (_currentDeck[0], _currentDeck[swapIndex]) = (_currentDeck[swapIndex], _currentDeck[0]);
        }

        _deckIndex = 0;
    }
}
