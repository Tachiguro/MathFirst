namespace MathFirst.Application;

using MathFirst.Domain;

public sealed class PracticeOperationSelectionDraft
{
    private readonly HashSet<ArithmeticOperation> _enabledOperations;

    public PracticeOperationSelectionDraft(IEnumerable<ArithmeticOperation>? enabledOperations)
    {
        _enabledOperations = new HashSet<ArithmeticOperation>(
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(enabledOperations));
    }

    public IReadOnlyList<ArithmeticOperation> EnabledOperations =>
        PracticeOperationPreferencePolicy.AllOperations
            .Where(_enabledOperations.Contains)
            .ToArray();

    public bool IsSelected(ArithmeticOperation operation) => _enabledOperations.Contains(operation);

    public bool Toggle(ArithmeticOperation operation)
    {
        if (_enabledOperations.Contains(operation))
        {
            if (!PracticeOperationPreferencePolicy.CanToggleOperationOff(_enabledOperations, operation))
            {
                return false;
            }

            _enabledOperations.Remove(operation);
        }
        else
        {
            _enabledOperations.Add(operation);
        }

        return true;
    }

    public void Save(IPreferenceStore preferenceStore)
    {
        ArgumentNullException.ThrowIfNull(preferenceStore);
        foreach (var operation in EnabledOperations)
        {
            preferenceStore.SetOperationEnabled(operation, true);
        }

        foreach (var operation in PracticeOperationPreferencePolicy.AllOperations.Where(operation => !_enabledOperations.Contains(operation)))
        {
            preferenceStore.SetOperationEnabled(operation, false);
        }
    }
}
