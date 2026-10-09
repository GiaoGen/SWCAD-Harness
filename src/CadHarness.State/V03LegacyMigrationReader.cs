using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public sealed record LegacyMigrationRead(CadState State, CadProgram Program, ManagedStateCompanion Proposal);

public static class LegacyMigrationReader
{
    // Read-only contract preparation. No save, migration publish, or native API is called.
    public static LegacyMigrationRead Read(string statePath, string programPath, string rollbackPath)
    {
        try
        {
            statePath = Path.GetFullPath(statePath); programPath = Path.GetFullPath(programPath);
            ContractValidation.Require(Path.IsPathFullyQualified(rollbackPath), "Rollback path must be explicit and absolute.");
            var stateBefore = Hash(statePath); var programBefore = Hash(programPath);
            var state = new AtomicStateStore(statePath).Load();
            if (new FileInfo(programPath).Length > CadProgramJson.MaximumJsonBytes)
                throw new ContractException(V03FailureCodes.MigrationFailed, "Legacy program exceeds its byte bound.");
            var parsed = new CadProgramJson().Parse(File.ReadAllText(programPath, Encoding.UTF8));
            ContractValidation.Require(parsed.IsValid, "Legacy program is not strict v0.2.", V03FailureCodes.MigrationFailed);
            var program = parsed.Program!;
            var features = new System.Collections.Generic.Dictionary<string, OperationKind>(StringComparer.Ordinal);
            foreach (var op in program.Operations)
                if (OperationRegistry.Default.Get(op.Kind).CreatesSemanticEntity) features.Add(op.SemanticId!, op.Kind);
            ContractValidation.Require(state.Features.Count == features.Count && System.Linq.Enumerable.All(state.Features,
                f => features.TryGetValue(f.SemanticId, out var kind) && kind == f.Kind), "Legacy state/program managed feature ownership differs.", V03FailureCodes.MigrationFailed);
            ContractValidation.Require(stateBefore == Hash(statePath) && programBefore == Hash(programPath), "Source drift during migration read.", V03FailureCodes.SourceFileDrift);
            var audit = new MigrationAudit("0.3", "0.2", "0.3", statePath, stateBefore, programBefore,
                rollbackPath, V03FailureCodes.NativeValidationRequired, false, false);
            var proposal = new ManagedStateCompanion("0.3", ModelOrigin.Harness,
                new(statePath, stateBefore, "0.2"), new(programPath, programBefore, "0.2"), audit, ReopenStatus.Inspectable);
            ObservedStateValidation.Companion(proposal);
            return new(state, program, proposal);
        }
        catch (Exception error) when (error is StateException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new ContractException(V03FailureCodes.MigrationFailed, "Legacy read failed: " + error.Message); }
    }
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
}
