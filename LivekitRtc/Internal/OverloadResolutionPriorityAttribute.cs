// author: https://github.com/pabloFuente

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill of the .NET 9 attribute, which netstandard2.1 lacks: the C# 13+ compiler recognizes it
    /// by name. A deprecated overload kept for binary compatibility gets a lower priority, so a call that
    /// both overloads accept binds to the current one instead of raising the deprecation warning.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false
    )]
    internal sealed class OverloadResolutionPriorityAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute.
        /// </summary>
        /// <param name="priority">The priority; higher wins, the default is 0.</param>
        public OverloadResolutionPriorityAttribute(int priority)
        {
            Priority = priority;
        }

        /// <summary>
        /// Gets the priority.
        /// </summary>
        public int Priority { get; }
    }
}
