using Arch.Core;
using Collections.Pooled;

namespace Robust.Shared.GameObjects;

internal struct ArchetypeIterator
{
    private readonly PooledList<Archetype> _archetypes;

    internal ArchetypeIterator(PooledList<Archetype> archetypes)
    {
        _archetypes = archetypes;
    }

    public ArchetypeEnumerator GetEnumerator()
    {
        return new ArchetypeEnumerator(_archetypes);
    }
}

internal struct ArchetypeEnumerator
{
    private readonly PooledList<Archetype> _archetypes;
    private int _index;
    private Archetype _current = default!;

    public ArchetypeEnumerator(PooledList<Archetype> archetypes)
    {
        _archetypes = archetypes;
        _index = _archetypes.Count;
    }

    public bool MoveNext()
    {
        while (--_index >= 0)
        {
            // TODO ARCH is pooledList indexing slower than array indexing?
            _current = _archetypes[_index];
            if (_current.EntityCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    public Archetype Current => _current;
}
