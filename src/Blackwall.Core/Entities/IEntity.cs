namespace Blackwall.Core.Entities;

public interface IEntity {
    long Id { get; set; }
    DateTime CreatedAtUtc { get; set; }
}