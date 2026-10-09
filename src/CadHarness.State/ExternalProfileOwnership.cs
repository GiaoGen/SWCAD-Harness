namespace CadHarness.State.V03;

public static class ExternalProfileOwnership
{
    // An origin can expose ISketch and be a constraint parent; it is not the
    // consumed profile. Three-dimensional and other sketch subtypes stay unsupported.
    public static bool IsConsumingProfile(string nativeType) => nativeType == "ProfileFeature";
}
