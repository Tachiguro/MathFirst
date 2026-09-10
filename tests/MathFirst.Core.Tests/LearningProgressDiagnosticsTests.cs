namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Diagnostics;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class LearningProgressDiagnosticsTests
{
    [Fact]
    public void LearningProgressDiagnostics_ExposeIndependentCanonicalOperationRows()
    {
        var diagnostics = LearningProgressDiagnostics.Create(CreateProgression(), new ArithmeticCurriculum());

        Assert.Equal(57, diagnostics.PracticePosition);
        Assert.Collection(
            diagnostics.Operations,
            addition => Assert.Equal(
                new OperationProgressDiagnostics(ArithmeticOperation.Addition, 10, "ADD-P1-ANCHOR", 11), addition),
            subtraction => Assert.Equal(
                new OperationProgressDiagnostics(ArithmeticOperation.Subtraction, 10, "SUB-I11", 12), subtraction),
            multiplication => Assert.Equal(
                new OperationProgressDiagnostics(ArithmeticOperation.Multiplication, 10, "MUL-D11", 13), multiplication),
            division => Assert.Equal(
                new OperationProgressDiagnostics(ArithmeticOperation.Division, 10, "DIV-D11", 14), division));
    }

    [Fact]
    public void LearningProgressDiagnostics_AreValueEquivalentAndFailClosedForUnavailableBands()
    {
        var curriculum = new ArithmeticCurriculum();
        var equivalent = LearningProgressDiagnostics.Create(CreateProgression(), curriculum);
        var identical = LearningProgressDiagnostics.Create(CreateProgression(), curriculum);
        var unavailable = LearningProgressDiagnostics.Create(
            new LearnerProgression
            {
                PracticePosition = 57,
                OperationProgressions = new Dictionary<ArithmeticOperation, OperationProgression>
                {
                    [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 999, 11),
                    [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, 10, 12),
                    [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 10, 13),
                    [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 10, 14)
                }
            },
            curriculum);

        Assert.Equal(equivalent.PracticePosition, identical.PracticePosition);
        Assert.Equal(equivalent.Operations, identical.Operations);
        Assert.Equal(new OperationProgressDiagnostics(ArithmeticOperation.Addition, 999, null, 11), unavailable.Operations[0]);
        Assert.DoesNotContain(typeof(LearningProgressDiagnostics).GetProperties(), property =>
            property.Name.Contains("Level", StringComparison.Ordinal) ||
            property.Name.Contains("Percentage", StringComparison.Ordinal) ||
                   property.Name.Contains("Completion", StringComparison.Ordinal));
    }

    [Fact]
    public void LearningProgressDiagnostics_ExposeSafeOneBasedPresentationStagesInCanonicalOrder()
    {
        var diagnostics = LearningProgressDiagnostics.Create(CreateProgression(), new ArithmeticCurriculum());
        var stageProperty = typeof(OperationProgressDiagnostics).GetProperty("PresentationStage");

        Assert.NotNull(stageProperty);
        Assert.Equal(
            [11, 11, 11, 11],
            diagnostics.Operations.Select(operation => (int?)stageProperty!.GetValue(operation)).ToArray());

        var advancedMultiplication = new OperationProgressDiagnostics(
            ArithmeticOperation.Multiplication, 1, "MUL-D02", 12);
        var malformed = new OperationProgressDiagnostics(
            ArithmeticOperation.Division, -1, null, null);

        Assert.Equal(2, (int?)stageProperty.GetValue(advancedMultiplication));
        Assert.Null((int?)stageProperty.GetValue(malformed));
    }

    [Fact]
    public void LocalizationService_DoesNotExposeObsoleteGlobalProgressionStrings()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var english = (Dictionary<string, string>)typeof(LocalizationService)
            .GetField("EnglishStrings", flags)!
            .GetValue(null)!;

        Assert.DoesNotContain(
            english.Keys,
            key => key.Contains("Checkpoint", StringComparison.Ordinal) ||
                   key.Contains("MixedRound", StringComparison.Ordinal) ||
                   key.Contains("AllIntroductionsComplete", StringComparison.Ordinal) ||
                   key is "Diagnostics_CurrentOperation" or "Diagnostics_CurrentPhase" or "Diagnostics_CurrentRange");
    }

    private static LearnerProgression CreateProgression() => new()
    {
        PracticePosition = 57,
        OperationProgressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 10, 11),
            [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, 10, 12),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 10, 13),
            [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 10, 14)
        }
    };
}
