using System;
using System.IO;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public static class DocumentIdentityAdapter
{
    private const string DocumentProperty = "CADHarness.v0.2.DocumentId";
    private const string ConfigurationProperty = "CADHarness.v0.2.ConfigurationId";

    public static void EnsurePersistentIds(SolidWorksExecutionContext context)
    {
        context.CheckThread();
        var document = context.Document;
        var configuration = document.ConfigurationManager.ActiveConfiguration.Name;
        Ensure((ICustomPropertyManager)document.Extension.CustomPropertyManager[""], DocumentProperty);
        Ensure((ICustomPropertyManager)document.Extension.CustomPropertyManager[configuration], ConfigurationProperty);
    }

    public static DocumentIdentity Read(SolidWorksExecutionContext context)
        => ReadIdentity(context, false);

    // Unsaved Parts still have native document/configuration GUIDs. An empty
    // path explicitly represents live state; durable M3 capture still needs Save.
    public static DocumentIdentity ReadForBinding(SolidWorksExecutionContext context)
        => ReadIdentity(context, true);

    private static DocumentIdentity ReadIdentity(SolidWorksExecutionContext context, bool allowUnsaved)
    {
        context.CheckThread();
        var document = context.Document;
        var configuration = document.ConfigurationManager.ActiveConfiguration.Name;
        var path = document.GetPathName();
        if (string.IsNullOrWhiteSpace(path) && !allowUnsaved) throw new StateException("OPERATION_PRECONDITION_FAILED", "Save the managed Part before capturing durable state.");
        return new(ReadGuid((ICustomPropertyManager)document.Extension.CustomPropertyManager[""], DocumentProperty),
            ReadGuid((ICustomPropertyManager)document.Extension.CustomPropertyManager[configuration], ConfigurationProperty),
            configuration, string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path));
    }

    public static void Verify(SolidWorksExecutionContext context, DocumentIdentity expected)
    {
        if (!expected.Matches(Read(context))) throw new StateException("STALE_REFERENCE", "Document or configuration identity differs; no reference was bound.");
    }
    private static void Ensure(ICustomPropertyManager properties, string name)
    {
        var status = properties.Get6(name, false, out var value, out _, out _, out _);
        if (status != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
        {
            if (!Guid.TryParseExact(value, "D", out var guid) || guid == Guid.Empty)
                throw new StateException("STALE_REFERENCE", "Existing managed identity property is invalid.");
            return;
        }
        var id = Guid.NewGuid();
        if (properties.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, id.ToString("D"),
                (int)swCustomPropertyAddOption_e.swCustomPropertyOnlyIfNew) != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
            throw new StateException("OPERATION_PRECONDITION_FAILED", "Cannot register native document/configuration identity.");
    }
    private static Guid ReadGuid(ICustomPropertyManager properties, string name)
    {
        var status = properties.Get6(name, false, out var value, out _, out _, out _);
        if (status == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent || !Guid.TryParseExact(value, "D", out var guid) || guid == Guid.Empty)
            throw new StateException("STALE_REFERENCE", "Native document/configuration identity is missing or invalid.");
        return guid;
    }
}
