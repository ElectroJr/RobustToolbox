using Arch.Core;

namespace Robust.Shared.GameObjects;

internal struct ArchChunkIterator
{
    private readonly ArchetypeEnumerator _archetypes;

    internal ArchChunkIterator(in ArchetypeEnumerator archetypes)
    {
        _archetypes = archetypes;
    }

    public ArchChunkEnumerator GetEnumerator()
    {
        return new ArchChunkEnumerator(_archetypes);
    }
}

internal struct ArchChunkEnumerator
{
    private ArchetypeEnumerator _archetypes;
    private int _chunkIndex;
    private Chunk _current;
    public readonly Chunk Current => _current;

    internal ArchChunkEnumerator(in ArchetypeEnumerator archetypes)
    {
        _archetypes = archetypes;

        if (_archetypes.MoveNext())
        {
            _chunkIndex = _archetypes.Current.ChunkCount;
        }
    }

    public bool MoveNext()
    {
        if (--_chunkIndex >= 0)
        {
            _current = _archetypes.Current.GetChunk(_chunkIndex);
            if (_current.Size > 0)
                return true;
        }

        if (!_archetypes.MoveNext())
        {
            return false;
        }

        _chunkIndex = _archetypes.Current.ChunkCount;
        return MoveNext();
    }
}

internal static partial class QueryExtensions
{
    internal static ArchChunkIterator ChunkIterator(this in Query query, World world)
    {
        var archetypeEnumerator = new ArchetypeEnumerator(query.Matches);
        return new ArchChunkIterator(in archetypeEnumerator);
    }
}
