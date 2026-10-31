namespace MathFirst.Domain.Curriculum;

/// <summary>
/// A curriculum whose acquisition ownership and number space follow directly from a fact's operands. Every band
/// frontier it produces is already disjoint from all earlier bands, so ownership and the guided number-space ceiling
/// are computed in constant time instead of by materializing every earlier band (Issue #126).
/// </summary>
public interface IAnalyticCurriculumModel
{
    /// <summary>
    /// Resolves the single acquisition-owner band of a canonical <c>FactId</c>. Returns false for identifiers this
    /// curriculum never generates, including non-canonical spellings and operands beyond its supported resource bound.
    /// </summary>
    bool TryGetOwnerBandIndex(string factId, out int ownerBandIndex);

    /// <summary>
    /// The largest operand or result of any fact owned by bands <c>0..throughBandIndex</c>.
    /// </summary>
    int GetNumberSpaceCeiling(int throughBandIndex);
}
