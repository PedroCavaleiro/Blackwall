namespace Blackwall.Core.Entities;

public class EntityBase: IEntity {
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}