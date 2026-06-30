using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Blackwall.Infrastructure.ValueGenerators;

public sealed class UtcNowGenerator: ValueGenerator<DateTime> {

    /// <inheritdoc/>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc/>
    public override DateTime Next(EntityEntry entry) => DateTime.UtcNow;

}