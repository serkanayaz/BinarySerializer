﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BinarySerialization.Graph.ValueGraph;

namespace BinarySerialization.Graph.TypeGraph
{
    internal class ObjectTypeNode : ContainerTypeNode
    {
        private const BindingFlags MemberBindingFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

        private const BindingFlags ConstructorBindingFlags = BindingFlags.Instance | BindingFlags.Public;
        private readonly object _subTypesLock = new object();
        private IDictionary<Type, SubTypeInfo> _subTypes;
        private bool _subTypesConstructed;

        public ObjectTypeNode(TypeNode parent, Type type) : base(parent, type)
        {
        }

        public ObjectTypeNode(TypeNode parent, Type parentType, MemberInfo memberInfo)
            : base(parent, parentType, memberInfo)
        {
        }

        public IDictionary<Type, object> SubTypeKeys { get; private set; }

        private void Construct()
        {
            if (IgnoreAttribute != null)
                return;

            if (_subTypesConstructed)
                return;

            _subTypes = new Dictionary<Type, SubTypeInfo> {{Type, CreateSubTypeInfo(Type)}};

            /* Add subtypes, if any */
            if (SubtypeAttributes == null || SubtypeAttributes.Count <= 0)
                return;

            /* Get subtype keys */
            if (SubtypeBinding.BindingMode == BindingMode.TwoWay)
                SubTypeKeys = SubtypeAttributes.ToDictionary(attribute => attribute.Subtype,
                    attribute => attribute.Value);

            /* Generate subtype children */
            var subTypes = SubtypeAttributes.Select(attribute => attribute.Subtype);

            foreach (var subType in subTypes)
                GenerateChildren(subType);

            _subTypesConstructed = true;
        }

        public override ValueNode CreateSerializerOverride(ValueNode parent)
        {
            return new ObjectValueNode(parent, Name, this);
        }

        public SubTypeInfo GetSubType(Type type)
        {
            lock (_subTypesLock)
            {
                Construct();

                /* If this is a type we've never seen before let's update our reference types. */
                if (!_subTypes.ContainsKey(type))
                    _subTypes.Add(type, CreateSubTypeInfo(type));

                return _subTypes[type];
            }
        }

        private void GenerateChildren(Type type)
        {
            lock (_subTypesLock)
            {
                if (!_subTypes.ContainsKey(type))
                {
                    _subTypes.Add(type, CreateSubTypeInfo(type));
                }
            }
        }

        private SubTypeInfo CreateSubTypeInfo(Type type)
        {
            var constructors = type.GetConstructors(ConstructorBindingFlags);
            var children = GenerateChildrenImpl(type);
            return new SubTypeInfo(constructors, children);
        }

        private IEnumerable<TypeNode> GenerateChildrenImpl(Type parentType)
        {
            IEnumerable<MemberInfo> properties = parentType.GetProperties(MemberBindingFlags);
            IEnumerable<MemberInfo> fields = parentType.GetFields(MemberBindingFlags);
            var all = properties.Union(fields);

            var children =
                all.Select(memberInfo => GenerateChild(parentType, memberInfo)).OrderBy(child => child.Order).ToList();

            var serializableChildren = children.Where(child => child.IgnoreAttribute == null).ToList();

            if (serializableChildren.Count > 1)
            {
                var unorderedChild = serializableChildren.FirstOrDefault(child => child.Order == null);

                if (unorderedChild != null)
                    throw new InvalidOperationException(
                        string.Format(
                            "'{0}' does not have a FieldOrder attribute.  " +
                            "All serializable fields or properties in a class with more than one member must specify a FieldOrder attribute.",
                            unorderedChild.Name));

                var orderGroups = serializableChildren.GroupBy(child => child.Order);

                if (orderGroups.Count() != serializableChildren.Count)
                    throw new InvalidOperationException("All fields must have a unique order number.");
            }

            if (parentType.BaseType != null)
            {
                var baseChildren = GenerateChildrenImpl(parentType.BaseType);
                return baseChildren.Concat(children);
            }

            return children;
        }
    }
}