using System;
using HotChocolate.Language;
using HotChocolate.Properties;

#nullable enable

namespace HotChocolate.Types
{
    public abstract class FloatTypeBase<TRuntimeType>
       : ScalarType<TRuntimeType>
       where TRuntimeType : IComparable
    {
        protected FloatTypeBase(
            NameString name,
            TRuntimeType min,
            TRuntimeType max,
            BindingBehavior bind = BindingBehavior.Explicit)
           : base(name, bind)
        {
            MinValue = min;
            MaxValue = max;
        }

        public TRuntimeType MinValue { get; }

        public TRuntimeType MaxValue { get; }

        public override bool IsInstanceOfType(IValueNode valueSyntax)
        {
            if (valueSyntax is null)
            {
                throw new ArgumentNullException(nameof(valueSyntax));
            }

            if (valueSyntax is NullValueNode)
            {
                return true;
            }

            if (valueSyntax is FloatValueNode floatLiteral && IsInstanceOfType(floatLiteral))
            {
                return true;
            }

            // Input coercion rules specify that float values can be coerced
            // from IntValueNode and FloatValueNode:
            // http://facebook.github.io/graphql/June2018/#sec-Float
            if (valueSyntax is IntValueNode intLiteral && IsInstanceOfType(intLiteral))
            {
                return true;
            }

            return false;
        }

        protected virtual bool IsInstanceOfType(IFloatValueLiteral valueSyntax)
        {
            return IsInstanceOfType(ParseLiteral(valueSyntax));
        }

        public override object? ParseLiteral(IValueNode valueSyntax, bool withDefaults = true)
        {
            if (valueSyntax is null)
            {
                throw new ArgumentNullException(nameof(valueSyntax));
            }

            if (valueSyntax is NullValueNode)
            {
                return null;
            }

            if (valueSyntax is FloatValueNode floatLiteral)
            {
                return ParseLiteral(floatLiteral);
            }

            // Input coercion rules specify that float values can be coerced
            // from IntValueNode and FloatValueNode:
            // http://facebook.github.io/graphql/June2018/#sec-Float

            if (valueSyntax is IntValueNode intLiteral)
            {
                return ParseLiteral(intLiteral);
            }

            throw new SerializationException(
                TypeResourceHelper.Scalar_Cannot_ParseLiteral(Name, valueSyntax.GetType()),
                this);
        }

        protected abstract TRuntimeType ParseLiteral(IFloatValueLiteral valueSyntax);

        protected virtual bool IsInstanceOfType(TRuntimeType value)
        {
            if (value.CompareTo(MinValue) == -1 || value.CompareTo(MaxValue) == 1)
            {
                return false;
            }

            return true;
        }

        public override IValueNode ParseValue(object? runtimeValue)
        {
            if (runtimeValue is null)
            {
                return NullValueNode.Default;
            }

            if (runtimeValue is TRuntimeType casted && IsInstanceOfType(casted))
            {
                return ParseValue(casted);
            }

            throw new SerializationException(
                TypeResourceHelper.Scalar_Cannot_ParseValue(Name, runtimeValue.GetType()),
                this);
        }

        protected abstract FloatValueNode ParseValue(TRuntimeType runtimeValue);

        public sealed override IValueNode ParseResult(object? resultValue)
        {
            if (resultValue is null)
            {
                return NullValueNode.Default;
            }

            if (resultValue is TRuntimeType casted && IsInstanceOfType(casted))
            {
                return ParseResult(casted);
            }

            if (TryConvertSerialized(resultValue, ValueKind.Integer, out TRuntimeType c)
                && IsInstanceOfType(c))
            {
                return ParseValue(c);
            }

            throw new SerializationException(
                TypeResourceHelper.Scalar_Cannot_ParseResult(Name, resultValue.GetType()),
                this);
        }

        public override bool TrySerialize(object? runtimeValue, out object? resultValue)
        {
            if (runtimeValue is null)
            {
                resultValue = null;
                return true;
            }

            if (runtimeValue is TRuntimeType casted && IsInstanceOfType(casted))
            {
                resultValue = runtimeValue;
                return true;
            }

            resultValue = null;
            return false;
        }

        public override bool TryDeserialize(object? resultValue, out object? runtimeValue)
        {
            if (resultValue is null)
            {
                runtimeValue = null;
                return true;
            }

            if (resultValue is TRuntimeType casted && IsInstanceOfType(casted))
            {
                runtimeValue = resultValue;
                return true;
            }

            if ((TryConvertSerialized(resultValue, ValueKind.Float, out TRuntimeType c)
                || TryConvertSerialized(resultValue, ValueKind.Integer, out c))
                && IsInstanceOfType(c))
            {
                runtimeValue = c;
                return true;
            }

            runtimeValue = null;
            return false;
        }
    }
}
