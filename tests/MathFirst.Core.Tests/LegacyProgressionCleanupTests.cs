namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Domain;

public sealed class LegacyProgressionCleanupTests
{
    [Fact]
    public void LearnerProgression_DoesNotExposeGlobalV4ProgressionMembers()
    {
        var names = typeof(LearnerProgression).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("CurrentOperation", names);
        Assert.DoesNotContain("CurrentIntroductionTurn", names);
        Assert.DoesNotContain("CurrentMaxOperand", names);
        Assert.DoesNotContain("OperationMaxOperands", names);
        Assert.DoesNotContain("CompletedCheckpointLevel", names);
        Assert.DoesNotContain("ActiveCheckpointLevel", names);
        Assert.DoesNotContain("CheckpointAttemptCount", names);
        Assert.DoesNotContain("CheckpointCorrectCount", names);
    }

    [Fact]
    public void LearningPolicy_DoesNotExposeV4GlobalProgressionPolicy()
    {
        var methods = typeof(LearningPolicy).GetMethods().Select(method => method.Name);

        Assert.DoesNotContain("DetermineProgressionPhase", methods);
        Assert.DoesNotContain("SynchronizeProgression", methods);
        Assert.DoesNotContain("GetIntroducedOperationsCount", methods);
    }

    [Fact]
    public void AdaptivePracticeSelector_DoesNotExposeLegacyV4SelectorEntryPoint()
    {
        var methods = typeof(AdaptivePracticeSelector).GetMethods().Select(method => method.Name);

        Assert.DoesNotContain("SelectNextFact", methods);
    }
}
