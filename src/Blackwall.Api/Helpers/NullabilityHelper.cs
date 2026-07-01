using System.Reflection;

namespace Blackwall.Api.Helpers;

public static class NullabilityHelper {

    /// <summary>
    /// Determines whether the given property is a nullable reference type
    /// by inspecting its nullability metadata via <see cref="NullabilityInfoContext"/>.
    /// </summary>
    /// <param name="prop">The property to inspect.</param>
    /// <returns>
    /// <c>true</c> if the property's write or read nullability state is marked as nullable;
    /// otherwise <c>false</c>.
    /// </returns>
    public static bool IsNullableReferenceType(PropertyInfo prop) {
        var nullabilityContext = new NullabilityInfoContext();
        var info = nullabilityContext.Create(prop);
        return info.WriteState == NullabilityState.Nullable
               || info.ReadState == NullabilityState.Nullable;
    }

}