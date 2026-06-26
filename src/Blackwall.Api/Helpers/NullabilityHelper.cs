using System.Reflection;

namespace Blackwall.Api.Helpers;

public static class NullabilityHelper {

    public static bool IsNullableReferenceType(PropertyInfo prop) {
        var nullabilityContext = new NullabilityInfoContext();
        var info = nullabilityContext.Create(prop);
        return info.WriteState == NullabilityState.Nullable
               || info.ReadState == NullabilityState.Nullable;
    }

}