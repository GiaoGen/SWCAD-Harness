using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir.V03;

// Bounded orthogonal half-section. Axial stations are immutable; only declared
// positive radial drivers move. Relationships are typed offsets, never code.
public static class RadialProfilePolicy
{
    public static void Validate(ClosedSketch sketch)
    {
        var profile = sketch.RadialProfile!;
        ContractValidation.Require(profile.Version == 1 && profile.Drivers.Count is > 0 and <= 24 &&
            profile.Points.Count == sketch.Points.Count && profile.Relations.Count <= 24,
            "Radial profile version/inventory invalid.");
        ContractValidation.Require(sketch.Loops.Count == 1 && !sketch.Loops[0].Inner && sketch.Entities.Count == 1 &&
            sketch.Entities[0].Kind == SketchEntityKind.Polyline && sketch.Constraints.Count == 0,
            "Radial profile needs one closed orthogonal polyline; no additional constraint declarations.");
        ContractValidation.Unique(profile.Drivers.Select(d => d.Id), "radial driver");
        ContractValidation.Unique(profile.Points.Select(p => p.PointId), "radial point binding");
        ContractValidation.Unique(profile.Relations.Select(r => r.Id), "radial relation");
        ContractValidation.Unique(profile.Relations.Select(r => r.TargetDriver), "derived radial target");
        ContractValidation.Require(profile.Drivers.All(d => ContractValidation.Id(d.Id) && ContractValidation.Length(d.Radius.Millimeters)) &&
            profile.Relations.All(r => ContractValidation.Id(r.Id) && ContractValidation.Length(r.Offset.Millimeters)), "Invalid radial identity/value.");
        var drivers = profile.Drivers.ToDictionary(d => d.Id);
        foreach (var p in sketch.Points)
        {
            var binding = profile.Points.SingleOrDefault(b => b.PointId == p.Id);
            ContractValidation.Require(binding is not null && (binding.DriverId is null ? p.Y.Millimeters == 0 :
                drivers.TryGetValue(binding.DriverId, out var driver) && Math.Abs(p.Y.Millimeters - driver.Radius.Millimeters) <= PlanarSketchPreflight.ToleranceMm),
                "Point does not agree with its declared radius driver or zero baseline.");
        }
        var points = sketch.Points.ToDictionary(p => p.Id);
        var ordered = sketch.Entities[0].Points.Select(id => points[id]).ToArray();
        var bound = profile.Points.ToDictionary(b => b.PointId, b => b.DriverId);
        var used = new HashSet<string>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var a = ordered[i]; var b = ordered[(i + 1) % ordered.Length];
            var horizontal = Math.Abs(a.Y.Millimeters - b.Y.Millimeters) <= PlanarSketchPreflight.ToleranceMm;
            var vertical = Math.Abs(a.X.Millimeters - b.X.Millimeters) <= PlanarSketchPreflight.ToleranceMm;
            ContractValidation.Require(horizontal != vertical, "Radial section needs nonzero orthogonal edges.");
            if (horizontal)
            {
                ContractValidation.Require(bound[a.Id] == bound[b.Id], "One radial rail cannot have different drivers.");
                if (bound[a.Id] is { } id) used.Add(id);
            }
        }
        ContractValidation.Require(used.SetEquals(drivers.Keys), "Each radius driver needs an axial rail.");
        var pending = profile.Relations.ToList(); var known = drivers.Keys.Except(pending.Select(r => r.TargetDriver)).ToHashSet();
        while (pending.Count != 0)
        {
            var ready = pending.Where(r => known.Contains(r.SourceDriver)).ToArray();
            ContractValidation.Require(ready.Length != 0, "Radial relationships are cyclic or unresolved.");
            foreach (var r in ready)
            {
                ContractValidation.Require(drivers.ContainsKey(r.TargetDriver) && drivers.ContainsKey(r.SourceDriver) &&
                    Math.Abs(drivers[r.TargetDriver].Radius.Millimeters - (drivers[r.SourceDriver].Radius.Millimeters - r.Offset.Millimeters)) <= PlanarSketchPreflight.ToleranceMm,
                    "Radial offset relation differs from geometry.");
                pending.Remove(r); known.Add(r.TargetDriver);
            }
        }
    }
    public static ClosedSketch Edit(ClosedSketch sketch, string driverId, double expected, double radius)
    {
        Validate(sketch);
        var p = sketch.RadialProfile!; var driver = p.Drivers.SingleOrDefault(d => d.Id == driverId);
        ContractValidation.Require(driver is not null && !p.Relations.Any(r => r.TargetDriver == driverId) &&
            Math.Abs(driver.Radius.Millimeters - expected) <= PlanarSketchPreflight.ToleranceMm && ContractValidation.Length(radius),
            "Driver is missing, derived, stale or outside bounds.");
        var values = p.Drivers.ToDictionary(d => d.Id, d => d.Radius.Millimeters); values[driverId] = radius;
        var pending = p.Relations.ToList(); var known = values.Keys.Except(pending.Select(r => r.TargetDriver)).ToHashSet();
        while (pending.Count != 0)
            foreach (var r in pending.Where(r => known.Contains(r.SourceDriver)).ToArray())
            { values[r.TargetDriver] = values[r.SourceDriver] - r.Offset.Millimeters; known.Add(r.TargetDriver); pending.Remove(r); }
        var bindings = p.Points.ToDictionary(b => b.PointId);
        var changed = sketch with
        {
            Points = sketch.Points.Select(point => point with { Y = new(bindings[point.Id].DriverId is { } id ? values[id] : 0) }).ToArray(),
            RadialProfile = p with { Drivers = p.Drivers.Select(d => d with { Radius = new(values[d.Id]) }).ToArray() }
        };
        _ = PlanarSketchPreflight.Prepare(changed); return changed;
    }
    public static IReadOnlyList<string> NativeOrder(ClosedSketch before, ClosedSketch after)
    {
        var initial = before.RadialProfile!.Drivers.ToDictionary(d => d.Id, d => d.Radius.Millimeters);
        var final = after.RadialProfile!.Drivers.ToDictionary(d => d.Id, d => d.Radius.Millimeters);
        ContractValidation.Require(initial.Keys.ToHashSet().SetEquals(final.Keys), "Native radial edit cannot change parameter identity.");
        var pending = initial.Keys.Where(id => initial[id] != final[id]).ToList();
        // Prefer larger radii first when growing, smaller first when shrinking.
        // Each candidate is still checked: a final-valid edit must not collapse
        // an intermediate rail or wall and invalidate its native references.
        var growing = pending.All(id => final[id] > initial[id]);
        pending = (growing ? pending.OrderByDescending(id => initial[id]) : pending.OrderBy(id => initial[id])).ToList();
        var values = new Dictionary<string, double>(initial); var order = new List<string>();
        var pointOwners = before.RadialProfile.Points.ToDictionary(p => p.PointId, p => p.DriverId);
        while (pending.Count != 0)
        {
            string? selected = null;
            foreach (var id in pending)
            {
                var proposed = new Dictionary<string, double>(values) { [id] = final[id] };
                var staged = before with {
                    Points = before.Points.Select(p => p with { Y = new(pointOwners[p.Id] is { } owner ? proposed[owner] : 0) }).ToArray(),
                    RadialProfile = before.RadialProfile with { Drivers = before.RadialProfile.Drivers.Select(d => d with { Radius = new(proposed[d.Id]) }).ToArray(), Relations = Array.Empty<RadialOffsetRelation>() }
                };
                try { _ = PlanarSketchPreflight.Prepare(staged); selected = id; break; }
                catch (ContractException) { }
            }
            ContractValidation.Require(selected is not null, "No proven safe radial intermediate order.", "UNSAFE_RADIAL_EDIT_ORDER");
            values[selected!] = final[selected!]; order.Add(selected!); pending.Remove(selected!);
        }
        return order;
    }
}
