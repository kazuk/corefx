// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using Validation;

namespace System.Collections.Immutable
{
    /// <summary>
    /// A node in the AVL tree storing key/value pairs with Int32 keys.
    /// </summary>
    /// <remarks>
    /// This is a trimmed down version of <see cref="ImmutableSortedDictionary{TKey, TValue}.Node"/>
    /// with TKey fixed to be Int32.  This avoids multiple interface-based dispatches while examining
    /// each node in the tree during a lookup: an interface call to the comparer's Compare method,
    /// and then an interface call to Int32's IComparable's CompareTo method as part of
    /// the GenericComparer{Int32}'s Compare implementation.
    /// </remarks>
    [DebuggerDisplay("{key} = {value}")]
    internal sealed class SortedInt32KeyNode<TValue> : IBinaryTree
    {
        /// <summary>
        /// The default empty node.
        /// </summary>
        internal static readonly SortedInt32KeyNode<TValue> EmptyNode = new SortedInt32KeyNode<TValue>();

        /// <summary>
        /// The Int32 key associated with this node.
        /// </summary>
        private readonly int _key;

        /// <summary>
        /// The value associated with this node.
        /// </summary>
        /// <remarks>
        /// Sadly, this field could be readonly but doing so breaks serialization due to bug: 
        /// http://connect.microsoft.com/VisualStudio/feedback/details/312970/weird-argumentexception-when-deserializing-field-in-typedreferences-cannot-be-static-or-init-only
        /// </remarks>
        private TValue _value;

        /// <summary>
        /// A value indicating whether this node has been frozen (made immutable).
        /// </summary>
        /// <remarks>
        /// Nodes must be frozen before ever being observed by a wrapping collection type
        /// to protect collections from further mutations.
        /// </remarks>
        private bool _frozen;

        /// <summary>
        /// The depth of the tree beneath this node.
        /// </summary>
        private byte _height; // AVL tree height <= ~1.44 * log2(numNodes + 2)

        /// <summary>
        /// The left tree.
        /// </summary>
        private SortedInt32KeyNode<TValue> _left;

        /// <summary>
        /// The right tree.
        /// </summary>
        private SortedInt32KeyNode<TValue> _right;

        /// <summary>
        /// Initializes a new instance of the <see cref="SortedInt32KeyNode{TValue}"/> class that is pre-frozen.
        /// </summary>
        private SortedInt32KeyNode()
        {
            _frozen = true; // the empty node is *always* frozen.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SortedInt32KeyNode{TValue}"/> class that is not yet frozen.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        /// <param name="frozen">Whether this node is prefrozen.</param>
        private SortedInt32KeyNode(int key, TValue value, SortedInt32KeyNode<TValue> left, SortedInt32KeyNode<TValue> right, bool frozen = false)
        {
            Requires.NotNull(left, "left");
            Requires.NotNull(right, "right");
            Debug.Assert(!frozen || (left._frozen && right._frozen));

            _key = key;
            _value = value;
            _left = left;
            _right = right;
            _frozen = frozen;

            _height = checked((byte)(1 + Math.Max(left._height, right._height)));
        }

        /// <summary>
        /// Gets a value indicating whether this instance is empty.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is empty; otherwise, <c>false</c>.
        /// </value>
        public bool IsEmpty { get { return _left == null; } }

        /// <summary>
        /// Gets the height of the tree beneath this node.
        /// </summary>
        public int Height { get { return _height; } }

        /// <summary>
        /// Gets the left branch of this node.
        /// </summary>
        public SortedInt32KeyNode<TValue> Left { get { return _left; } }

        /// <summary>
        /// Gets the right branch of this node.
        /// </summary>
        public SortedInt32KeyNode<TValue> Right { get { return _right; } }

        /// <summary>
        /// Gets the left branch of this node.
        /// </summary>
        IBinaryTree IBinaryTree.Left { get { return _left; } }

        /// <summary>
        /// Gets the right branch of this node.
        /// </summary>
        IBinaryTree IBinaryTree.Right { get { return _right; } }

        /// <summary>
        /// Gets the number of elements contained by this node and below.
        /// </summary>
        int IBinaryTree.Count
        {
            get { throw new NotSupportedException(); }
        }

        /// <summary>
        /// Gets the value represented by the current node.
        /// </summary>
        public KeyValuePair<int, TValue> Value
        {
            get { return new KeyValuePair<int, TValue>(_key, _value); }
        }

        /// <summary>
        /// Gets the values.
        /// </summary>
        internal IEnumerable<TValue> Values
        {
            get
            {
                foreach (var pair in this)
                {
                    yield return pair.Value;
                }
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        /// Adds the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="valueComparer">The value comparer.</param>
        /// <param name="replacedExistingValue">Receives a value indicating whether an existing value was replaced.</param>
        /// <param name="mutated">Receives a value indicating whether this node tree has mutated because of this operation.</param>
        internal SortedInt32KeyNode<TValue> SetItem(int key, TValue value, IEqualityComparer<TValue> valueComparer, out bool replacedExistingValue, out bool mutated)
        {
            Requires.NotNull(valueComparer, "valueComparer");

            return this.SetOrAdd(key, value, valueComparer, true, out replacedExistingValue, out mutated);
        }

        /// <summary>
        /// Removes the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="mutated">Receives a value indicating whether this node tree has mutated because of this operation.</param>
        /// <returns>The new AVL tree.</returns>
        internal SortedInt32KeyNode<TValue> Remove(int key, out bool mutated)
        {
            return this.RemoveRecursive(key, out mutated);
        }

        /// <summary>
        /// Gets the value or default.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The value.</returns>
        [Pure]
        internal TValue GetValueOrDefault(int key)
        {
            var match = this.Search(key);
            return match.IsEmpty ? default(TValue) : match._value;
        }

        /// <summary>
        /// Tries to get the value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>True if the key was found.</returns>
        [Pure]
        internal bool TryGetValue(int key, out TValue value)
        {
            var match = this.Search(key);
            if (match.IsEmpty)
            {
                value = default(TValue);
                return false;
            }
            else
            {
                value = match._value;
                return true;
            }
        }

        /// <summary>
        /// Freezes this node and all descendent nodes so that any mutations require a new instance of the nodes.
        /// </summary>
        internal void Freeze(Action<KeyValuePair<int, TValue>> freezeAction = null)
        {
            // If this node is frozen, all its descendents must already be frozen.
            if (!_frozen)
            {
                if (freezeAction != null)
                {
                    freezeAction(new KeyValuePair<int, TValue>(_key, _value));
                }

                _left.Freeze(freezeAction);
                _right.Freeze(freezeAction);
                _frozen = true;
            }
        }

        /// <summary>
        /// AVL rotate left operation.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>The rotated tree.</returns>
        private static SortedInt32KeyNode<TValue> RotateLeft(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);

            if (tree._right.IsEmpty)
            {
                return tree;
            }

            var right = tree._right;
            return right.Mutate(left: tree.Mutate(right: right._left));
        }

        /// <summary>
        /// AVL rotate right operation.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>The rotated tree.</returns>
        private static SortedInt32KeyNode<TValue> RotateRight(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);

            if (tree._left.IsEmpty)
            {
                return tree;
            }

            var left = tree._left;
            return left.Mutate(right: tree.Mutate(left: left._right));
        }

        /// <summary>
        /// AVL rotate double-left operation.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>The rotated tree.</returns>
        private static SortedInt32KeyNode<TValue> DoubleLeft(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);

            if (tree._right.IsEmpty)
            {
                return tree;
            }

            SortedInt32KeyNode<TValue> rotatedRightChild = tree.Mutate(right: RotateRight(tree._right));
            return RotateLeft(rotatedRightChild);
        }

        /// <summary>
        /// AVL rotate double-right operation.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>The rotated tree.</returns>
        private static SortedInt32KeyNode<TValue> DoubleRight(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);

            if (tree._left.IsEmpty)
            {
                return tree;
            }

            SortedInt32KeyNode<TValue> rotatedLeftChild = tree.Mutate(left: RotateLeft(tree._left));
            return RotateRight(rotatedLeftChild);
        }

        /// <summary>
        /// Returns a value indicating whether the tree is in balance.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>0 if the tree is in balance, a positive integer if the right side is heavy, or a negative integer if the left side is heavy.</returns>
        [Pure]
        private static int Balance(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);

            return tree._right._height - tree._left._height;
        }

        /// <summary>
        /// Determines whether the specified tree is right heavy.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>
        /// <c>true</c> if [is right heavy] [the specified tree]; otherwise, <c>false</c>.
        /// </returns>
        [Pure]
        private static bool IsRightHeavy(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);
            return Balance(tree) >= 2;
        }

        /// <summary>
        /// Determines whether the specified tree is left heavy.
        /// </summary>
        [Pure]
        private static bool IsLeftHeavy(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);
            return Balance(tree) <= -2;
        }

        /// <summary>
        /// Balances the specified tree.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <returns>A balanced tree.</returns>
        [Pure]
        private static SortedInt32KeyNode<TValue> MakeBalanced(SortedInt32KeyNode<TValue> tree)
        {
            Requires.NotNull(tree, "tree");
            Debug.Assert(!tree.IsEmpty);

            if (IsRightHeavy(tree))
            {
                return Balance(tree._right) < 0 ? DoubleLeft(tree) : RotateLeft(tree);
            }

            if (IsLeftHeavy(tree))
            {
                return Balance(tree._left) > 0 ? DoubleRight(tree) : RotateRight(tree);
            }

            return tree;
        }

        /// <summary>
        /// Creates a node tree that contains the contents of a list.
        /// </summary>
        /// <param name="items">An indexable list with the contents that the new node tree should contain.</param>
        /// <param name="start">The starting index within <paramref name="items"/> that should be captured by the node tree.</param>
        /// <param name="length">The number of elements from <paramref name="items"/> that should be captured by the node tree.</param>
        /// <returns>The root of the created node tree.</returns>
        [Pure]
        private static SortedInt32KeyNode<TValue> NodeTreeFromList(IOrderedCollection<KeyValuePair<int, TValue>> items, int start, int length)
        {
            Requires.NotNull(items, "items");
            Requires.Range(start >= 0, "start");
            Requires.Range(length >= 0, "length");

            if (length == 0)
            {
                return EmptyNode;
            }

            int rightCount = (length - 1) / 2;
            int leftCount = (length - 1) - rightCount;
            SortedInt32KeyNode<TValue> left = NodeTreeFromList(items, start, leftCount);
            SortedInt32KeyNode<TValue> right = NodeTreeFromList(items, start + leftCount + 1, rightCount);
            var item = items[start + leftCount];
            return new SortedInt32KeyNode<TValue>(item.Key, item.Value, left, right, true);
        }

        /// <summary>
        /// Adds the specified key. Callers are expected to have validated arguments.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="valueComparer">The value comparer.</param>
        /// <param name="overwriteExistingValue">if <c>true</c>, an existing key=value pair will be overwritten with the new one.</param>
        /// <param name="replacedExistingValue">Receives a value indicating whether an existing value was replaced.</param>
        /// <param name="mutated">Receives a value indicating whether this node tree has mutated because of this operation.</param>
        /// <returns>The new AVL tree.</returns>
        private SortedInt32KeyNode<TValue> SetOrAdd(int key, TValue value, IEqualityComparer<TValue> valueComparer, bool overwriteExistingValue, out bool replacedExistingValue, out bool mutated)
        {
            // Arg validation skipped in this private method because it's recursive and the tax
            // of revalidating arguments on each recursive call is significant.
            // All our callers are therefore required to have done input validation.
            replacedExistingValue = false;
            if (this.IsEmpty)
            {
                mutated = true;
                return new SortedInt32KeyNode<TValue>(key, value, this, this);
            }
            else
            {
                SortedInt32KeyNode<TValue> result = this;
                if (key > _key)
                {
                    var newRight = _right.SetOrAdd(key, value, valueComparer, overwriteExistingValue, out replacedExistingValue, out mutated);
                    if (mutated)
                    {
                        result = this.Mutate(right: newRight);
                    }
                }
                else if (key < _key)
                {
                    var newLeft = _left.SetOrAdd(key, value, valueComparer, overwriteExistingValue, out replacedExistingValue, out mutated);
                    if (mutated)
                    {
                        result = this.Mutate(left: newLeft);
                    }
                }
                else
                {
                    if (valueComparer.Equals(_value, value))
                    {
                        mutated = false;
                        return this;
                    }
                    else if (overwriteExistingValue)
                    {
                        mutated = true;
                        replacedExistingValue = true;
                        result = new SortedInt32KeyNode<TValue>(key, value, _left, _right);
                    }
                    else
                    {
                        throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, Strings.DuplicateKey, key));
                    }
                }

                return mutated ? MakeBalanced(result) : result;
            }
        }

        /// <summary>
        /// Removes the specified key. Callers are expected to validate arguments.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="mutated">Receives a value indicating whether this node tree has mutated because of this operation.</param>
        /// <returns>The new AVL tree.</returns>
        private SortedInt32KeyNode<TValue> RemoveRecursive(int key, out bool mutated)
        {
            if (this.IsEmpty)
            {
                mutated = false;
                return this;
            }
            else
            {
                SortedInt32KeyNode<TValue> result = this;
                if (key == _key)
                {
                    // We have a match.
                    mutated = true;

                    // If this is a leaf, just remove it 
                    // by returning Empty.  If we have only one child,
                    // replace the node with the child.
                    if (_right.IsEmpty && _left.IsEmpty)
                    {
                        result = EmptyNode;
                    }
                    else if (_right.IsEmpty && !_left.IsEmpty)
                    {
                        result = _left;
                    }
                    else if (!_right.IsEmpty && _left.IsEmpty)
                    {
                        result = _right;
                    }
                    else
                    {
                        // We have two children. Remove the next-highest node and replace
                        // this node with it.
                        var successor = _right;
                        while (!successor._left.IsEmpty)
                        {
                            successor = successor._left;
                        }

                        bool dummyMutated;
                        var newRight = _right.Remove(successor._key, out dummyMutated);
                        result = successor.Mutate(left: _left, right: newRight);
                    }
                }
                else if (key < _key)
                {
                    var newLeft = _left.Remove(key, out mutated);
                    if (mutated)
                    {
                        result = this.Mutate(left: newLeft);
                    }
                }
                else
                {
                    var newRight = _right.Remove(key, out mutated);
                    if (mutated)
                    {
                        result = this.Mutate(right: newRight);
                    }
                }

                return result.IsEmpty ? result : MakeBalanced(result);
            }
        }


        /// <summary>
        /// Creates a node mutation, either by mutating this node (if not yet frozen) or by creating a clone of this node
        /// with the described changes.
        /// </summary>
        /// <param name="left">The left branch of the mutated node.</param>
        /// <param name="right">The right branch of the mutated node.</param>
        /// <returns>The mutated (or created) node.</returns>
        private SortedInt32KeyNode<TValue> Mutate(SortedInt32KeyNode<TValue> left = null, SortedInt32KeyNode<TValue> right = null)
        {
            if (_frozen)
            {
                return new SortedInt32KeyNode<TValue>(_key, _value, left ?? _left, right ?? _right);
            }
            else
            {
                if (left != null)
                {
                    _left = left;
                }

                if (right != null)
                {
                    _right = right;
                }

                _height = checked((byte)(1 + Math.Max(_left._height, _right._height)));
                return this;
            }
        }

        /// <summary>
        /// Searches the specified key. Callers are expected to validate arguments.
        /// </summary>
        /// <param name="key">The key.</param>
        [Pure]
        private SortedInt32KeyNode<TValue> Search(int key)
        {
            if (this.IsEmpty || key == _key)
            {
                return this;
            }

            if (key > _key)
            {
                return _right.Search(key);
            }

            return _left.Search(key);
        }

        /// <summary>
        /// Enumerates the contents of a binary tree.
        /// </summary>
        /// <remarks>
        /// This struct can and should be kept in exact sync with the other binary tree enumerators: 
        /// ImmutableList.Enumerator, ImmutableSortedMap.Enumerator, and ImmutableSortedSet.Enumerator.
        /// 
        /// CAUTION: when this enumerator is actually used as a valuetype (not boxed) do NOT copy it by assigning to a second variable 
        /// or by passing it to another method.  When this enumerator is disposed of it returns a mutable reference type stack to a resource pool,
        /// and if the value type enumerator is copied (which can easily happen unintentionally if you pass the value around) there is a risk
        /// that a stack that has already been returned to the resource pool may still be in use by one of the enumerator copies, leading to data
        /// corruption and/or exceptions.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public struct Enumerator : IEnumerator<KeyValuePair<int, TValue>>, ISecurePooledObjectUser
        {
            /// <summary>
            /// The resource pool of reusable mutable stacks for purposes of enumeration.
            /// </summary>
            /// <remarks>
            /// We utilize this resource pool to make "allocation free" enumeration achievable.
            /// </remarks>
            private static readonly SecureObjectPool<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>, Enumerator> s_enumeratingStacks =
                new SecureObjectPool<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>, Enumerator>();

            /// <summary>
            /// A unique ID for this instance of this enumerator.
            /// Used to protect pooled objects from use after they are recycled.
            /// </summary>
            private readonly int _poolUserId;

            /// <summary>
            /// The set being enumerated.
            /// </summary>
            private SortedInt32KeyNode<TValue> _root;

            /// <summary>
            /// The stack to use for enumerating the binary tree.
            /// </summary>
            private SecurePooledObject<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>> _stack;

            /// <summary>
            /// The node currently selected.
            /// </summary>
            private SortedInt32KeyNode<TValue> _current;

            /// <summary>
            /// Initializes an Enumerator structure.
            /// </summary>
            /// <param name="root">The root of the set to be enumerated.</param>
            internal Enumerator(SortedInt32KeyNode<TValue> root)
            {
                Requires.NotNull(root, "root");

                _root = root;
                _current = null;
                _poolUserId = SecureObjectPool.NewId();
                _stack = null;
                if (!_root.IsEmpty)
                {
                    if (!s_enumeratingStacks.TryTake(this, out _stack))
                    {
                        _stack = s_enumeratingStacks.PrepNew(this, new Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>(root.Height));
                    }

                    this.PushLeft(_root);
                }
            }

            /// <summary>
            /// The current element.
            /// </summary>
            public KeyValuePair<int, TValue> Current
            {
                get
                {
                    this.ThrowIfDisposed();
                    if (_current != null)
                    {
                        return _current.Value;
                    }

                    throw new InvalidOperationException();
                }
            }

            /// <inheritdoc/>
            int ISecurePooledObjectUser.PoolUserId
            {
                get { return _poolUserId; }
            }

            /// <summary>
            /// The current element.
            /// </summary>
            object IEnumerator.Current
            {
                get { return this.Current; }
            }

            /// <summary>
            /// Disposes of this enumerator and returns the stack reference to the resource pool.
            /// </summary>
            public void Dispose()
            {
                _root = null;
                _current = null;
                Stack<RefAsValueType<SortedInt32KeyNode<TValue>>> stack;
                if (_stack != null && _stack.TryUse(ref this, out stack))
                {
                    stack.ClearFastWhenEmpty();
                    s_enumeratingStacks.TryAdd(this, _stack);
                }

                _stack = null;
            }

            /// <summary>
            /// Advances enumeration to the next element.
            /// </summary>
            /// <returns>A value indicating whether there is another element in the enumeration.</returns>
            public bool MoveNext()
            {
                this.ThrowIfDisposed();

                if (_stack != null)
                {
                    var stack = _stack.Use(ref this);
                    if (stack.Count > 0)
                    {
                        SortedInt32KeyNode<TValue> n = stack.Pop().Value;
                        _current = n;
                        this.PushLeft(n.Right);
                        return true;
                    }
                }

                _current = null;
                return false;
            }

            /// <summary>
            /// Restarts enumeration.
            /// </summary>
            public void Reset()
            {
                this.ThrowIfDisposed();

                _current = null;
                if (_stack != null)
                {
                    var stack = _stack.Use(ref this);
                    stack.ClearFastWhenEmpty();
                    this.PushLeft(_root);
                }
            }

            /// <summary>
            /// Throws an ObjectDisposedException if this enumerator has been disposed.
            /// </summary>
            internal void ThrowIfDisposed()
            {
                // Since this is a struct, copies might not have been marked as disposed.
                // But the stack we share across those copies would know.
                // This trick only works when we have a non-null stack.
                // For enumerators of empty collections, there isn't any natural
                // way to know when a copy of the struct has been disposed of.

                if (_root == null || (_stack != null && !_stack.IsOwned(ref this)))
                {
                    Validation.Requires.FailObjectDisposed(this);
                }
            }

            /// <summary>
            /// Pushes this node and all its Left descendents onto the stack.
            /// </summary>
            /// <param name="node">The starting node to push onto the stack.</param>
            private void PushLeft(SortedInt32KeyNode<TValue> node)
            {
                Requires.NotNull(node, "node");
                var stack = _stack.Use(ref this);
                while (!node.IsEmpty)
                {
                    stack.Push(new RefAsValueType<SortedInt32KeyNode<TValue>>(node));
                    node = node.Left;
                }
            }
        }
    }
}
