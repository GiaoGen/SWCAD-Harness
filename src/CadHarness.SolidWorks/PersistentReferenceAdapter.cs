using System;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed record ReferenceResolution<T>(T? NativeObject, ReferenceHealth Health, int NativeStatus) where T : class;

public static class PersistentReferenceAdapter
{
    public static NativePersistentReference Capture(SolidWorksExecutionContext context, object native)
    {
        context.CheckThread();
        var payload = context.Document.Extension.GetPersistReference3(native) as byte[];
        if (payload is null || payload.Length == 0) throw new StateException("STALE_REFERENCE", "Native persistent-reference capture failed.");
        var reference = new NativePersistentReference(Convert.ToBase64String(payload));
        StateValidation.DecodeReference(reference);
        return reference;
    }

    public static ReferenceResolution<T> Resolve<T>(SolidWorksExecutionContext context, NativePersistentReference reference) where T : class
    {
        context.CheckThread();
        var bytes = StateValidation.DecodeReference(reference);
        var native = context.Document.Extension.GetObjectByPersistReference3(bytes, out var status);
        var health = status == (int)swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok && native is T
            ? ReferenceHealth.Healthy
            : (status & (int)swPersistReferencedObjectStates_e.swPersistReferencedObject_Deleted) != 0 ? ReferenceHealth.Deleted
            : (status & (int)swPersistReferencedObjectStates_e.swPersistReferencedObject_Suppressed) != 0 ? ReferenceHealth.Suppressed
            : ReferenceHealth.Stale;
        return new(health == ReferenceHealth.Healthy ? (T)native : null, health, status);
    }
}
