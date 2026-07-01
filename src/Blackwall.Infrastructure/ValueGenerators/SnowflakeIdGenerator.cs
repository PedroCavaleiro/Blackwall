using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Sharpflake;

namespace Blackwall.Infrastructure.ValueGenerators;

public sealed class SnowflakeIdGenerator: ValueGenerator<long> {

    /// <inheritdoc/>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc/>
    public override long Next(EntityEntry entry) {
        var generator = new SnowflakeGenerator();
        return generator.GenerateSnowflake();
    }

}