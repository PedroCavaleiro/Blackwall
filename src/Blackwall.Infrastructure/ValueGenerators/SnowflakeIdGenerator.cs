using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Sharpflake;

namespace Blackwall.Infrastructure.ValueGenerators;

public sealed class SnowflakeIdGenerator: ValueGenerator<long> {

    private static readonly SnowflakeGenerator Generator = new();

    /// <inheritdoc/>
    public override bool GeneratesTemporaryValues => true;

    /// <inheritdoc/>
    public override long Next(EntityEntry entry) {
        return Generator.GenerateSnowflake();
    }

}