namespace CadHarness.State.V03;

public static class ExternalProfileOwnership
{
    public static bool IsLocalOriginRelation(bool coincident, bool twoPoints, bool localOrigin,
        bool ownCircleCenter, bool sameSketch, bool exactReferences, bool inContext) =>
        coincident && twoPoints && localOrigin && ownCircleCenter && sameSketch && exactReferences && !inContext;
    // An origin can expose ISketch and be a constraint parent; it is not the
    // consumed profile. Three-dimensional and other sketch subtypes stay unsupported.
    public static bool IsConsumingProfile(string nativeType) => nativeType == "ProfileFeature";
}
