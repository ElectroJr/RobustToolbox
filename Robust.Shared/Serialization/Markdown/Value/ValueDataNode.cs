using System.Globalization;
using JetBrains.Annotations;
using YamlDotNet.RepresentationModel;

namespace Robust.Shared.Serialization.Markdown.Value
{
    public sealed class ValueDataNode : DataNode<ValueDataNode>
    {
        /// <summary>
        ///     The special string that represents null in yaml.
        /// </summary>
        public const string NullString = "null";

        public static ValueDataNode Null = new(NullString);

        public ValueDataNode() : this(string.Empty) {}

        public ValueDataNode(string value) : base(NodeMark.Invalid, NodeMark.Invalid)
        {
            Value = value;
        }

        public ValueDataNode(YamlScalarNode node) : base(node.Start, node.End)
        {
            Value = node.Value ?? string.Empty;
            Tag = node.Tag;
        }

        /// <summary>
        ///     Checks whether the value of this data node corresponds to the special string(s) used to represent null.
        /// </summary>
        /// <remarks>
        ///     Currently this only looks for "null", but in case more null strings are supported in the future (e.g.,
        ///     "~"), this property should be used rather than explicitly checking the string.
        /// </remarks>
        public bool IsNull => Value == NullString;

        public string Value { get; set; }

        public override bool IsEmpty => Value == string.Empty;

        public override ValueDataNode Copy()
        {
            return new(Value)
            {
                Tag = Tag,
                Start = Start,
                End = End
            };
        }

        public override ValueDataNode? Except(ValueDataNode node)
        {
            return node.Value == Value ? null : Copy();
        }

        public override ValueDataNode PushInheritance(ValueDataNode node)
        {
            return Copy();
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ValueDataNode node)
                return false;

            return node.Value == Value
                || (IsNull && node.IsNull); // explicitly checking null rather than comparing values to support more than one null-string.
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        [Pure]
        public int AsInt()
        {
            return int.Parse(Value, CultureInfo.InvariantCulture);
        }

        [Pure]
        public float AsFloat()
        {
            return float.Parse(Value, CultureInfo.InvariantCulture);
        }

        [Pure]
        public bool AsBool()
        {
            return bool.Parse(Value);
        }
    }
}
