using System.Collections.Generic;

namespace Core.Math;

/// <summary>
/// Interface for mathematical formula evaluation engine.
/// Handles formula loading, building, and evaluation with parameter support.
/// </summary>
public interface IMathEngine
{
    /// <summary>
    /// Builds a math expression from a formula definition.
    /// </summary>
    /// <param name="formulaName">Name of the formula to build</param>
    /// <param name="inputValue">Input value for the formula</param>
    /// <param name="paramOverrides">Optional parameter overrides</param>
    /// <returns>Built math expression ready for evaluation</returns>
    /// <exception cref="InvalidOperationException">Thrown if formula is not found or invalid</exception>
    MathExpression BuildFromFormula(
        string formulaName,
        float inputValue,
        Dictionary<string, float>? paramOverrides = null);

    /// <summary>
    /// Gets a list of all available formula names.
    /// </summary>
    /// <returns>Enumerable of formula names</returns>
    IEnumerable<string> GetAvailableFormulas();

    /// <summary>
    /// Checks if a formula exists.
    /// </summary>
    /// <param name="formulaName">Name of the formula to check</param>
    /// <returns>True if the formula exists, false otherwise</returns>
    bool FormulaExists(string formulaName);

    /// <summary>
    /// Invalidates the formula cache, forcing reload on next access.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    /// <returns>Dictionary containing cache metrics</returns>
    Dictionary<string, object> GetCacheStats();

    /// <summary>
    /// Gets the description of a formula.
    /// </summary>
    /// <param name="formulaName">Name of the formula</param>
    /// <returns>Formula description or null if not found</returns>
    string? GetFormulaDescription(string formulaName);

    /// <summary>
    /// Gets the default parameters for a formula.
    /// </summary>
    /// <param name="formulaName">Name of the formula</param>
    /// <returns>Dictionary of default parameters or null if not found</returns>
    Dictionary<string, float>? GetFormulaDefaultParams(string formulaName);

    /// <summary>
    /// Gets the merged parameters for a formula (defaults + overrides).
    /// </summary>
    /// <param name="formulaName">Name of the formula</param>
    /// <param name="paramOverrides">Optional parameter overrides</param>
    /// <returns>Result containing dictionary of merged parameters, or failure if formula not found</returns>
    Common.Result<Dictionary<string, float>> GetMergedParams(string formulaName, Dictionary<string, float>? paramOverrides = null);

    /// <summary>
    /// Gets the origin information for all formulas.
    /// </summary>
    /// <returns>Dictionary mapping formula names to their origin configs</returns>
    Dictionary<string, string> GetFormulaOrigins();
}
