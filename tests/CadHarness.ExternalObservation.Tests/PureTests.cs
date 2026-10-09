using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;

internal static class PureTests
{
    private sealed class Source : IExternalObservationSource
    {
        internal List<ExternalInventoryNode> Nodes = new();
        internal Dictionary<string, ExternalFeatureFacts> Facts = new();
        internal int Reads, Enumerated;
        public IEnumerable<ExternalInventoryNode> Inventory() { foreach (var node in Nodes) { Enumerated++; yield return node; } }
        public ExternalFeatureFacts Read(ExternalInventoryNode node, int allowance) { Reads++; return Facts[node.CaptureKey]; }
    }
    private static readonly ObservationEvidence[] Evidence = { new(EvidenceSource.NativeDefinition, "test native evidence") };
    private static ExternalInventoryNode Node(string key, bool reference = true, ObservationHealth health = ObservationHealth.Healthy) =>
        new(key, "same size and same displayed name", "Boss", health, reference ? new(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("persistent:" + key))) : null, Evidence);
    private static ExternalFeatureFacts Facts(string[]? parents = null, string[]? children = null, bool scalar = false) =>
        new(scalar ? NativeSubtype.StraightBlindBossExtrude : NativeSubtype.Unrecognized,
            scalar ? new[] { new ObservedParameter("placeholder", ParameterKey.ExtrusionDepth, ScalarUnit.Millimeter, 12,
                NativeAccessor.ExtrudeDepthDirection1, Evidence) } : Array.Empty<ObservedParameter>(),
            Array.Empty<GeometryObservation>(), parents, children, "read-only fixture");
    private static PartSelection Selection(string root, string config = "Default", string source = "original.SLDPRT")
    {
        var hash = new string('a',64);
        return ExternalObservation.Selection(new(Path.Combine(root, source), hash, 20), new(Path.Combine(root, "copy.SLDPRT"), hash, 20), config);
    }
    private static Source Fixture()
    {
        var source = new Source(); source.Nodes.AddRange(new[] { Node("boss"), Node("unknown"), Node("suppressed", health: ObservationHealth.Suppressed) });
        source.Facts["boss"] = Facts(Array.Empty<string>(), new[] { "unknown" }, true);
        source.Facts["unknown"] = Facts(new[] { "boss" }, Array.Empty<string>());
        source.Facts["suppressed"] = Facts(null, null);
        return source;
    }
    internal static int Run(string root)
    {
        var tests = new List<object>(); var failed = 0;
        void Test(string name, Action action)
        {
            try { action(); tests.Add(new { name, passed = true, error = (string?)null }); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; tests.Add(new { name, passed = false, error = error.Message }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        ExternalObservationResult Capture(Source? s = null, ExternalObservationLimits? limits = null) => ExternalObservation.Capture(Selection(root), s ?? Fixture(), limits, 3);
        Test("external overlay strict round trip; no synthetic program", () => {
            var result = Capture(); var json = ContractJson.Write(result.Model, ObservedStateValidation.Validate);
            var parsed = ContractJson.Read<ObservedModel>(json, ObservedStateValidation.Validate);
            Program.Require(parsed.Origin == ModelOrigin.External && parsed.Features.Count == 3 && parsed.Features.All(f => f.EditSupport == EditSupport.ReadOnly), "Read-only origin lost.");
        });
        Test("inventory and native count conventions are explicit", () => { var r = Capture(); Program.Require(r.Inventory.InventoryCount == 3 && r.Inventory.NativeInventoryCountExact && r.Inventory.NativeApiFeatureCount == 3, "Count mismatch."); });
        Test("unknown downstream stays visible with native edge", () => { var r = Capture().Model; Program.Require(r.Dependencies.Count == 1 && r.Features[1].Subtype == NativeSubtype.Unrecognized && r.Features[1].EditSupport == EditSupport.ReadOnly, "Unknown descendant discarded."); });
        Test("scalar value units and accessor evidence are retained", () => { var p = Capture().Model.Features[0].Parameters.Single(); Program.Require(p.Value == 12 && p.Unit == ScalarUnit.Millimeter && p.Evidence.Count > 0 && p.Accessor == NativeAccessor.ExtrudeDepthDirection1, "Scalar provenance lost."); });
        Test("suppression health and unknown dependencies are preserved", () => { var f = Capture().Model.Features[2]; Program.Require(f.Health == ObservationHealth.Suppressed && f.DependencyCompleteness == EvidenceCompleteness.Unknown && f.Parameters.Count == 0, "Suppression invented evidence."); });
        Test("explicit empty dependency arrays can prove known emptiness", () => Program.Require(Capture().Model.Features[0].DependencyCompleteness == EvidenceCompleteness.Known, "Known edges lost."));
        Test("missing dependencies are not an empty proven set", () => { var s = Fixture(); s.Facts["boss"] = Facts(); Program.Require(Capture(s).Model.Features[0].DependencyCompleteness == EvidenceCompleteness.Unknown, "Missing dependencies became known."); });
        Test("unchanged selected file and references retain identities", () => Program.Require(Capture().Model.Features.Select(f => f.SemanticId).SequenceEqual(Capture().Model.Features.Select(f => f.SemanticId)), "Unstable IDs."));
        Test("renames and reordering do not change reference identities", () => { var s = Fixture(); s.Nodes = s.Nodes.AsEnumerable().Reverse().Select(n => n with { DisplayName = "renamed" }).ToList(); var r = Capture(s); Program.Require(Capture().Model.Features.Select(f => f.SemanticId).ToHashSet().SetEquals(r.Model.Features.Select(f => f.SemanticId)), "Display identity used."); });
        Test("same-size same-name features do not collide", () => Program.Require(Capture().Model.Features.Select(f => f.SemanticId).Distinct().Count() == 3, "Ambiguous IDs."));
        Test("configuration changes form different identity namespaces", () => { var a = Capture().Model; var b = ExternalObservation.Capture(Selection(root,"Other"), Fixture()).Model; Program.Require(a.Selection.ConfigurationId != b.Selection.ConfigurationId && a.Features[0].SemanticId != b.Features[0].SemanticId, "Configuration identity reused."); });
        Test("Save As path is a different lineage without guessing", () => Program.Require(Selection(root).DocumentId != Selection(root,source:"other.SLDPRT").DocumentId, "Save As guessed."));
        Test("source drift cannot create an inspection selection", () => Refuse(() => ExternalObservation.Selection(new(Path.Combine(root,"original.SLDPRT"),new string('a',64),20),new(Path.Combine(root,"copy.SLDPRT"),new string('b',64),20),"Default"), V03FailureCodes.SourceFileDrift));
        Test("original cannot masquerade as its working copy", () => Refuse(() => ExternalObservation.Selection(new(Path.Combine(root,"original.SLDPRT"),new string('a',64),20),new(Path.Combine(root,"original.SLDPRT"),new string('a',64),20),"Default"), V03FailureCodes.ContractInvalid));
        Test("missing persistent refs are unbound diagnostic rows", () => { var s = Fixture(); s.Nodes[0] = Node("boss", false, ObservationHealth.Unknown); var f = Capture(s).Model.Features[0]; Program.Require(f.NativeReference is null && f.SemanticId.StartsWith("unbound_") && f.EditSupport == EditSupport.ReadOnly && f.SupportReason.Contains("not a binding target"), "Unbound row claimed identity."); });
        Test("feature overflow reads only bound plus one and no parameters", () => { var s = Fixture(); var r = Capture(s,new(Features:2)); Program.Require(!r.Model.InventoryComplete && r.Model.LimitOutcome == V03FailureCodes.ObservationLimitExceeded && s.Reads == 0 && s.Enumerated == 3 && r.Inventory.NativeInventoryCountLowerBound == 3 && !r.Inventory.NativeInventoryCountExact, "Inventory truncation disguised."); });
        Test("exact feature bound remains complete", () => Program.Require(Capture(limits:new(Features:3)).Model.InventoryComplete, "Exact bound refused."));
        Test("overflow nodes never advertise editable status", () => Program.Require(Capture(limits:new(Features:1)).Model.Features.All(f => f.EditSupport == EditSupport.ReadOnly && f.DependencyCompleteness == EvidenceCompleteness.Unknown), "Overflow editable."));
        Test("parameter overflow is partial and read-only", () => { var s = Fixture(); s.Facts["unknown"] = Facts(scalar:true); var r = Capture(s,new(Parameters:1)); Program.Require(!r.Model.InventoryComplete && r.Model.Features.All(f => f.EditSupport != EditSupport.Editable), "Parameter overflow hidden."); });
        Test("native geometry overflow is partial", () => { var s=Fixture(); s.Facts["boss"]=s.Facts["boss"] with {LimitExceeded=true}; Program.Require(!Capture(s).Model.InventoryComplete, "Native overflow hidden."); });
        Test("dangling dependency reduces completeness without inventing row", () => { var s=Fixture(); s.Facts["boss"]=Facts(new[]{"outside"},Array.Empty<string>()); var r=Capture(s).Model; Program.Require(r.Features.Count==3 && r.Features[0].DependencyCompleteness==EvidenceCompleteness.Unknown, "Invented dependency owner."); });
        Test("duplicate capture identity refuses", () => { var s=Fixture(); s.Nodes[1]=s.Nodes[0]; Refuse(()=>Capture(s),"AMBIGUOUS_NATIVE_INVENTORY"); });
        Test("duplicate persistent identity refuses", () => { var s=Fixture(); s.Nodes[1]=s.Nodes[1] with {Reference=s.Nodes[0].Reference}; Refuse(()=>Capture(s),"AMBIGUOUS_NATIVE_INVENTORY"); });
        Test("invalid persistent payload is not repaired by display name", () => {var s=Fixture();s.Nodes[0]=s.Nodes[0] with {Reference=new("not-base64")};Refuse(()=>Capture(s));});
        Test("native cycles refuse without constructing relation program", () => {var s=Fixture();s.Facts["unknown"]=Facts(new[]{"boss"},new[]{"boss"});Refuse(()=>Capture(s),V03FailureCodes.ContractInvalid);});
        Test("limits cannot widen frozen contracts", () => Refuse(()=>Capture(limits:new(Features:257)),V03FailureCodes.ContractInvalid));
        Test("all observed scalar targets fail executable external gate", () => {var r=Capture().Model;Refuse(()=>NativeQualificationCandidates.RequireExecutable(r,r.Features[0].SemanticId,ParameterKey.ExtrusionDepth),V03FailureCodes.ObservedOnlyTarget);});
        var output=Path.Combine(root,"artifacts","milestone13","pure",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));Directory.CreateDirectory(output);
        Program.WriteNew(Path.Combine(output,"results.json"),new{milestone=13,passed=tests.Count-failed,failed,nativeParts=0,nativeOpens=0,results=tests});
        Console.WriteLine($"M13 pure {tests.Count-failed}/{tests.Count}: {output}");return failed==0?0:1;
    }
    private static void Refuse(Action action,string? code=null)
    {
        try{action();}catch(Exception e) when(e is ICadFailure or ContractException){var actual=e is ICadFailure f?f.Code:((ContractException)e).Code;Program.Require(code is null||actual==code,$"Expected {code}, got {actual}.");return;}
        throw new InvalidOperationException("Expected a typed refusal.");
    }
}
