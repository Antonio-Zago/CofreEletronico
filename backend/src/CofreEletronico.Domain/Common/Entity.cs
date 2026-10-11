namespace CofreEletronico.Domain.Common;

/// <summary>
/// Base das entidades do domínio: identidade por Id, igualdade por identidade.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("O Id da entidade não pode ser vazio.", nameof(id));

        Id = id;
    }

    // Construtor sem parâmetros para materialização pelo EF Core.
    protected Entity()
    {
    }

    public Guid Id { get; private init; }

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
