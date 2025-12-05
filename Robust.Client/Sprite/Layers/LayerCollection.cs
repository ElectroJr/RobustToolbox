using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics.Clyde;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Sprite;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Client.Sprite.Layers;

/// <summary>
/// This layer represents a collection of other layers. When this layer is drawn, it will in turn draw each of it's children.
/// </summary>
[Virtual]
[Access(typeof(SpriteComponent), typeof(SpriteSystem), typeof(Clyde), typeof(BaseLayer))]
public class LayerCollection : BaseLayer
{
    [ViewVariables] internal readonly Dictionary<LayerKey, int> LayerMap = new();
    [ViewVariables] internal readonly List<BaseLayer> Layers = new();

    /// <inheritdoc cref="LayerCollectionData.DrawTogether"/>
    [ViewVariables] public bool DrawTogether;

    /// <inheritdoc cref="LayerCollectionData.GranularLayersRendering"/>
    [ViewVariables] public bool Granular;

    [ViewVariables] public override bool Animated => _isAnimated ?? UpdateIsAnimated();
    private bool? _isAnimated;

    [ViewVariables] internal override RsiDirectionType DirectionType => _directionType ?? UpdateDirectionType();

    private RsiDirectionType? _directionType;

    /// <summary>
    /// Whether the collection's layers should be sorted based on their <see cref="BaseLayer.DrawDepth"/> when rendering.
    /// </summary>
    /// <remarks>
    /// If false, the layers will instead get rendered in the order that they appear in <see cref="Layers"/>.
    /// </remarks>
    public bool Sorted;

    /// <summary>
    /// Whether the collection contains any layers with direction dependent draw depths (see <see cref="BaseLayer.DirectionalDrawDepths"/>).
    /// </summary>
    [ViewVariables] public bool DirectionalSorting;

    private bool _sortDirty = true;

    /// <summary>
    /// Layers sorted based on their draw depths.
    /// </summary>
    [ViewVariables] private List<BaseLayer>? _sorted;

    /// <summary>
    /// Layers sorted base on their direction-dependent draw depths, index by the <see cref="RsiDirection"/> enum.
    /// </summary>
    [ViewVariables] private List<BaseLayer>?[]? _sortedDirections;

    public BaseLayer this[int index] => Layers[index];
    public BaseLayer this[LayerKey key] => this[LayerMap[key]];

    public override Vector2 GetSize() => GetLocalBounds().Size;

    #region Rendering
    // These fields are only needed while rendering with GranularLayersRendering enabled
    // This is kinda cursed, but I can't think of a nice way of passing these around to nested layer collections.

    /// <summary>
    /// The entity's world rotation
    /// </summary>
    internal Angle WorldRot;

    /// <summary>
    /// The entity's current world position.
    /// </summary>
    internal Vector2 WorldPos;

    /// <summary>
    /// The parent collection's transform before applying the sprite entity transform
    /// </summary>
    internal Matrix3x2 ParentTransform;

    #endregion

    internal LayerCollection(SpriteSystem system, SpriteTreeSystem tree, EntityManager entMan, ISawmill log) :
        base(system, tree, entMan, log)
    {
    }

    internal LayerCollection(LayerCollection toClone) : base(toClone)
    {
        LayerMap = new(toClone.LayerMap);
        Layers = new(toClone.Layers.Count);
        foreach (var child in toClone.Layers)
        {
            var childClone = child.Clone();
            DebugTools.AssertEqual(childClone.GetType(), child.GetType());
            AddLayer(childClone);
        }
    }

    internal override BaseLayer Clone()
    {
        return new LayerCollection(this);
    }

    protected override Box2 CalculateLocalBounds()
    {
        var bounds = new Box2();
        foreach (var layer in Layers)
        {
            if (layer is {Drawn: true})
                bounds = bounds.Union(layer.CalculateRelativeBounds());
        }
        return bounds;
    }

    /// <summary>
    /// Attempt to get the layer corresponding to the given index.
    /// </summary>
    public bool TryGetLayer<T>(int index, [NotNullWhen(true)] out T? layer) where T : BaseLayer
    {
        layer = null;
        if (index < 0 || index >= Layers.Count)
            return false;

        var baseLayer = Layers[index];
        DebugTools.Assert(baseLayer.Owner == this || baseLayer.Collections != null && baseLayer.Collections.Contains(this));
        if (baseLayer is not T cast)
            return false;

        layer = cast;
        return true;
    }

    /// <summary>
    /// Attempt to get the layer corresponding to the given index.
    /// </summary>
    public bool TryGetLayer<T>(LayerKey key, [NotNullWhen(true)] out T? layer) where T : BaseLayer
    {
        layer = null;
        return LayerMap.TryGetValue(key, out var index) && TryGetLayer(index, out layer);
    }

    /// <summary>
    /// Attempt to resolve the layer index corresponding to the given key. This will log an error if there is no layer
    /// with the given key.
    /// </summary>
    public bool ResolveKey(LayerKey key, out int index)
    {
        if (LayerMap.TryGetValue(key, out index))
            return true;

        Log.Error($"Layer with key '{key}' does not exist on entity {EntMan.ToPrettyString(GetEntity())}! Trace:\n{Environment.StackTrace}");
        return false;
    }

    /// <summary>
    /// Attempt to resolve the layer corresponding to the given index. This will log an error if there is no layer
    /// with the given index.
    /// </summary>
    public bool ResolveLayer<T>(LayerKey key, [NotNullWhen(true)] out T? layer) where T : BaseLayer
    {
        layer = null;
        return ResolveKey(key, out var index) && ResolveLayer(index, out layer);
    }

    /// <summary>
    /// Attempt to resolve the layer corresponding to the given index. This will log an error if there is no layer
    /// with the given index.
    /// </summary>
    public bool ResolveLayer<T>(int index, [NotNullWhen(true)] out T? layer) where T :  BaseLayer
    {
        layer = null;
        if (index < 0 || index >= Layers.Count)
            return false;

        var baseLayer = Layers[index];

        DebugTools.Assert(baseLayer.Owner == this || baseLayer.Collections != null && baseLayer.Collections.Contains(this));
        if (baseLayer is T cast)
        {
            layer = cast;
            return true;
        }

        Log.Error($"Layer index '{index}' on entity {EntMan.ToPrettyString(GetEntity())} exist but was not of the expected type ({typeof(T).Name} vs {baseLayer?.GetType().Name}). Trace:\n{Environment.StackTrace}");
        return false;
    }

    public bool RemoveLayer(int index, [NotNullWhen(true)] out BaseLayer? removed)
    {
        removed = null;
        if (index < 0 || index >= Layers.Count)
            return false;

        if (Sorted)
        {
            var swappedWith = Layers.Count - 1;
            removed = Layers.RemoveSwap(index);
            foreach (var (key, value) in LayerMap)
            {
                if (value == index)
                    LayerMap.Remove(key);
                else if (value == swappedWith)
                    LayerMap[key] = index;
            }
        }
        else
        {
            removed = Layers[index];
            Layers.RemoveAt(index);
            foreach (var (key, value) in LayerMap)
            {
                if (value == index)
                    LayerMap.Remove(key);
                else if (value > index)
                    LayerMap[key] = value - 1;
            }
        }

        if (removed.Owner == this)
        {
            removed.Owner = null;
            DebugTools.Assert(removed.Collections == null || !removed.Collections.Contains(this));
        }
        else if (removed.Collections != null)
        {
            removed.Collections.Remove(this);
            if (removed.Collections.Count == 0)
                removed.Collections = null;
        }

        OnLayerRemoved(removed);
        return true;
    }

    public int AddLayer(BaseLayer layer, int? index = null)
    {
        RecursionCheck(layer);
        AddToCollection(layer);

        if (index is { } i && i != Layers.Count && !Sorted)
        {
            Layers.Insert(i, layer);
            foreach (var (key, value) in LayerMap)
            {
                if (value >= i)
                    LayerMap[key] = value + 1;
            }
        }
        else
        {
            index = Layers.Count;
            Layers.Add(layer);
        }

        OnLayerAdded(layer);
        return index.Value;
    }

    private void RecursionCheck(BaseLayer layer)
    {
        if (layer is not LayerCollection)
            return;

        if (this == layer)
            throw new Exception("Sprite layer recursion!"); // If this were content, I'd spawn a singularity

        Owner?.RecursionCheck(layer);
        if (Collections == null)
            return;

        foreach (var parent in Collections)
        {
            parent.RecursionCheck(layer);
        }
    }

    private void AddToCollection(BaseLayer layer)
    {
        // The majority of the time, layer.Owner should be null
        if (layer.Owner == null)
        {
            // Unless the layer has an RSI override, this will change the result of GetRsi().
            // But we let SpriteSystem handle refreshing RSI states.
            layer.Owner = this;
            return;
        }

        if (layer.Owner == this)
            throw new Exception("Layer is already owned by this collection?");

        layer.Collections ??= new();
        if (layer.Collections.Contains(this))
            throw new Exception("Layer is already part of this collection?");

        layer.Collections.Add(this);
    }

    /// <summary>
    /// This method is called whenever a layer is removed from the collection.
    /// </summary>
    [MustCallBase]
    protected virtual void OnLayerRemoved(BaseLayer removed)
    {
        InvalidateCache();
    }

    internal override void InvalidateCache()
    {
        base.InvalidateCache();
        _isAnimated = null;
        _directionType = null;
        _sortDirty = true;
    }

    [MemberNotNull(nameof(_isAnimated), nameof(_directionType))]
    private void Update()
    {
        var dir = 0;
        var isAnimated = false;
        foreach (var layer in Layers)
        {
            dir = Math.Max(dir, (int) layer.DirectionType);
            isAnimated |= layer.Animated;
        }

        _directionType = (RsiDirectionType) dir;
        _isAnimated = isAnimated;
    }

    private RsiDirectionType UpdateDirectionType()
    {
        Update();
        return _directionType.Value;
    }

    private bool UpdateIsAnimated()
    {
        Update();
        return _isAnimated.Value;
    }

    /// <summary>
    /// This method is called whenever a layer is added to the collection.
    /// </summary>
    [MustCallBase]
    protected virtual void OnLayerAdded(BaseLayer added)
    {
        Sorted |= added.DrawDepth != null;
        DirectionalSorting |= added.DirectionalDrawDepths != null;
        InvalidateCache();
    }

    /// <summary>
    /// Recursively call <see cref="InvalidateCache"/> on any child entities.
    /// </summary>
    internal void InvalidateCacheRecursive()
    {
        var children = new HashSet<BaseLayer>();
        RecursivelyAddChildren(this, children);
        foreach (var child in children)
        {
            child.InvalidateCache();
        }

        void RecursivelyAddChildren(LayerCollection collection, HashSet<BaseLayer> set)
        {
            foreach (var layer in collection.Layers)
            {
                // Layer collections will be implicitly invalidated when their children are.
                if (layer is LayerCollection coll)
                    RecursivelyAddChildren(coll, set);
                else
                    set.Add(layer);
            }
        }
    }


    internal ReadOnlySpan<BaseLayer> GetSortedLayers(RsiDirection direction)
    {
        if (!Sorted)
            return CollectionsMarshal.AsSpan(Layers);

        if (_sortDirty)
            Sort();

        return CollectionsMarshal.AsSpan(DirectionalSorting
            ? _sortedDirections![(int) direction]!
            : _sorted!);
    }

    private void Sort()
    {
        _sortDirty = false;
        _sorted ??= new();
        _sorted.Clear();
        _sorted.AddRange(Layers);
        _sorted.Sort();

        if (!DirectionalSorting)
            return;

        var numDir = DirectionCount;
        Array.Resize(ref _sortedDirections, numDir);
        for (var  i = 0; i < numDir; i++)
        {
            ref var list = ref _sortedDirections[i];
            list ??= new();
            list.Clear();
            list.AddRange(Layers);

            _comparer.Dir = i;
            list.Sort(_comparer);
        }
    }

    private DirectionalComparer _comparer = new();
    private sealed class DirectionalComparer : IComparer<BaseLayer>
    {
        public int Dir;

        public int Compare(BaseLayer? x, BaseLayer? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            var yDepth = y?.DirectionalDrawDepths == null ? y?.DrawDepth : y.DirectionalDrawDepths[Dir];
            var xDepth = x?.DirectionalDrawDepths == null ? x?.DrawDepth : x.DirectionalDrawDepths[Dir];

            return Nullable.Compare(xDepth, yDepth);
        }
    }
}
