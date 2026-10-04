using System;
using System.Collections.Generic;
using System.Threading;
using CadHarness.Ir;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

namespace CadHarness.SolidWorks;

public sealed record PreflightResult(bool IsValid, string? FailureCode, string Message)
{
    public static PreflightResult Success { get; } = new(true, null, "Preflight passed.");
}

public sealed record OperationExecutionResult(
    bool Succeeded, string? FailureCode, string Message, string? SemanticId,
    bool MutationStarted, bool RebuildSucceeded)
{
    // M6 introduces transaction rollback and state commit. M4 explicitly
    // reports that neither has occurred, including on partial native failure.
    public bool RollbackAttempted { get; init; }
    public bool RollbackSucceeded { get; init; }
    public bool StateCommitted { get; init; }
}

public interface IOperationBackendHandler
{
    OperationKind Kind { get; }
    PreflightResult Preflight(OperationNode operation);
    OperationExecutionResult Execute(SolidWorksExecutionContext context, OperationNode operation);
}

// STA-owned native context for one Part. Construction outputs live only in this
// session; CADState and document identity are provided by separate adapters.
public sealed partial class SolidWorksExecutionContext
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    public IModelDoc2 Document { get; }
    internal IFeature? CreatedFeature { get; set; }
    public SolidWorksExecutionContext(IModelDoc2 document)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("SOLIDWORKS execution requires an STA thread.");
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }
    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("The execution context must stay on its owning STA thread.");
    }
}

public static class Millimeters
{
    public static double ToMeters(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Length must be finite and positive.");
        var meters = value / 1000.0;
        if (meters <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Length is too small for native units.");
        return meters;
    }
}

public static class MilestoneTwoProgram
{
    public static PreflightResult Preflight(CadProgram program)
    {
        var validation = new ProgramValidator().Validate(program);
        if (!validation.IsValid)
        {
            var issue = validation.Issues[0];
            return new(false, issue.Code, issue.Path + ": " + issue.Message);
        }
        if (program.Operations.Count != 1 || program.Relations.Count != 0)
            return new(false, FailureCodes.OperationUnsupported, "Milestone 2 supports exactly one operation and no design relations.");
        return new CreateExtrudeHandler().Preflight(program.Operations[0]);
    }
}

internal sealed class NativeOperationException : Exception, CadHarness.State.ICadFailure
{
    public string Code { get; }
    internal NativeOperationException(string code, string message) : base(message) => Code = code;
}

internal static class NativeGeometry
{
    internal static IReadOnlyList<IBody2> SolidBodies(IModelDoc2 document)
    {
        var bodies = ((IPartDoc)document).GetBodies2((int)swBodyType_e.swSolidBody, false) as Array;
        var result = new List<IBody2>();
        if (bodies is not null)
            foreach (var body in bodies) result.Add((IBody2)body);
        return result.AsReadOnly();
    }
    internal static double[] Doubles(object values)
    {
        if (values is not Array array) throw new NativeOperationException("GEOMETRY_INVALID", "Native geometry returned no numeric array.");
        var result = new double[array.Length];
        for (var i = 0; i < array.Length; i++) result[i] = Convert.ToDouble(array.GetValue(i));
        return result;
    }
}
