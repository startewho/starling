namespace Starling.Js.Runtime;

/// <summary>
/// A hidden class ("shape") for the fast-path storage of an ordinary object's
/// <em>string-keyed data properties</em>. Shapes form a transition tree rooted
/// at <see cref="Root"/>: adding a property transitions to a child shape that is
/// cached on the parent, so two objects that gain the same properties (same
/// keys, same order, same attributes) end up sharing the <em>same</em> Shape
/// instance. That shared identity is what makes an inline cache's
/// <c>ReferenceEquals(obj.Shape, cachedShape)</c> check meaningful across many
/// objects.
/// </summary>
/// <remarks>
/// <para>Scope is deliberately narrow. A Shape describes only string-keyed DATA
/// properties. Accessors, deletion, attribute redefinition, sealing/freezing,
/// and non-extensible adds all push the object into "dictionary mode"
/// (<c>JsObject</c> with a null shape), where the legacy dictionary storage runs
/// unchanged. Symbol-keyed properties never enter a shape. This keeps the fast
/// path small and the intricate spec validators untouched.</para>
/// <para>Each data property occupies exactly one slot in the object's slot
/// array; the slot index equals the property's position in creation order
/// (<see cref="AddedSlot"/>). Slot indices are stable across the transition
/// tree — a child never moves a parent's slots — so a cached (shape, slot) pair
/// stays valid for the life of that shape.</para>
/// <para><b>Thread-safety.</b> Shapes are shared across threads (generator/async
/// bodies run on worker threads against the same compiled functions, which build
/// the same shapes). The lazily-built transition cache and flattened lookup
/// table are guarded by <see cref="_gate"/>; the immutable per-shape data
/// (parent, key, flags, slot count) needs no lock. Inline-cache hot reads never
/// touch a Shape's mutable state — they only compare references and index the
/// object's slot array.</para>
/// </remarks>
internal sealed class Shape
{
    // Attribute bits — mirror PropertyDescriptor's layout for data properties.
    // (Accessor (bit 3) never appears in a shape; accessors force dictionary mode.)
    internal const byte Writable = 1 << 0;
    internal const byte Enumerable = 1 << 1;
    internal const byte Configurable = 1 << 2;
    internal const byte DefaultData = Writable | Enumerable | Configurable;

    /// <summary>The process-wide empty root shape. Every fresh ordinary object
    /// starts here.</summary>
    public static readonly Shape Root = new();

    private readonly Shape? _parent;
    private readonly string? _key;   // key added relative to _parent; null only for the root
    private readonly byte _flags;    // attributes of the added data property

    /// <summary>Number of slots an object of this shape uses — equivalently the
    /// number of own string data properties, equivalently the depth in the
    /// transition tree.</summary>
    public int SlotCount { get; }

    private readonly object _gate = new();
    private Dictionary<TransitionKey, Shape>? _transitions;   // lazy child cache
    private Dictionary<string, ShapeProp>? _table;            // lazy flattened lookup

    private Shape() { SlotCount = 0; }

    private Shape(Shape parent, string key, byte flags)
    {
        _parent = parent;
        _key = key;
        _flags = flags;
        SlotCount = parent.SlotCount + 1;
    }

    /// <summary>The slot the property added by this shape occupies. Equal to the
    /// parent's slot count, so slots fill densely 0,1,2,… in creation order.</summary>
    public int AddedSlot => SlotCount - 1;

    /// <summary>Return the child shape reached by adding string data property
    /// <paramref name="key"/> with the given attribute <paramref name="flags"/>,
    /// creating and caching it on first use. Shared, so the same add from the
    /// same parent always yields the same child.</summary>
    public Shape Transition(string key, byte flags)
    {
        var tk = new TransitionKey(key, flags);
        lock (_gate)
        {
            _transitions ??= new Dictionary<TransitionKey, Shape>();
            if (_transitions.TryGetValue(tk, out var existing)) return existing;
            var child = new Shape(this, key, flags);
            _transitions[tk] = child;
            return child;
        }
    }

    /// <summary>Look up a string key in this shape. Returns false if absent.</summary>
    public bool TryGet(string key, out ShapeProp prop)
    {
        return Table.TryGetValue(key, out prop);
    }

    public bool Contains(string key) => Table.ContainsKey(key);

    private Dictionary<string, ShapeProp> Table
    {
        get
        {
            var t = _table;
            if (t is not null) return t;
            lock (_gate)
            {
                if (_table is not null) return _table;
                if (_parent is null)
                {
                    t = new Dictionary<string, ShapeProp>(StringComparer.Ordinal);
                }
                else
                {
                    // Copy the parent's flattened table and add our one entry.
                    // O(n) once per shape, then cached and shared by every object
                    // of this shape.
                    t = new Dictionary<string, ShapeProp>(_parent.Table, StringComparer.Ordinal);
                    t[_key!] = new ShapeProp(AddedSlot, _flags);
                }
                _table = t;
                return t;
            }
        }
    }

    /// <summary>The own string keys in creation (slot) order — the
    /// transition chain from root to here, oldest first.</summary>
    public IReadOnlyList<string> OrderedKeys()
    {
        if (SlotCount == 0) return System.Array.Empty<string>();
        var keys = new string[SlotCount];
        var s = this;
        while (s._key is not null)
        {
            keys[s.AddedSlot] = s._key;
            s = s._parent!;
        }
        return keys;
    }

    private readonly struct TransitionKey : System.IEquatable<TransitionKey>
    {
        private readonly string _key;
        private readonly byte _flags;
        public TransitionKey(string key, byte flags) { _key = key; _flags = flags; }
        public bool Equals(TransitionKey other)
            => _flags == other._flags && string.Equals(_key, other._key, System.StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is TransitionKey o && Equals(o);
        public override int GetHashCode()
            => System.HashCode.Combine(System.StringComparer.Ordinal.GetHashCode(_key), _flags);
    }
}

/// <summary>A property's location and attributes within a <see cref="Shape"/>:
/// its slot index plus the data-attribute flags.</summary>
internal readonly struct ShapeProp
{
    public readonly int Slot;
    public readonly byte Flags;
    public ShapeProp(int slot, byte flags) { Slot = slot; Flags = flags; }
    public bool Writable => (Flags & Shape.Writable) != 0;
    public bool Enumerable => (Flags & Shape.Enumerable) != 0;
    public bool Configurable => (Flags & Shape.Configurable) != 0;
}

/// <summary>
/// Per-call-site monomorphic inline cache for a property read (and, later,
/// write). Records the <see cref="Shape"/> an object had the last time this
/// bytecode site ran and the slot the property occupied. A cached read is valid
/// when <c>ReferenceEquals(obj.Shape, Shape)</c> — any structural change to an
/// object replaces its shape, so a stale entry simply misses and refills. A
/// null <see cref="Shape"/> is an empty (never-filled) cache.
/// </summary>
/// <remarks>Stored as a value type in <c>Chunk.Caches</c> (no per-entry
/// allocation). The engine's generator/async worker threads execute a shared
/// chunk cooperatively — only one thread runs at a time, handed off through
/// event barriers — so cache reads and refills never truly race.</remarks>
internal struct InlineCache
{
    public Shape? Shape;
    public int Slot;
}
