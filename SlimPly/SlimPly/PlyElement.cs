namespace SlimPly
{
    /// <summary>Single property entry in a PLY element.</summary>
    public readonly record struct PlyProperty(string Name, string Type);

    /// <summary>
    /// Represents a PLY element header, such as "vertex" or "chunk".
    /// </summary>
    /// <remarks>
    /// A PLY element defines a group of properties and their count within a PLY file.
    /// Each element has a name, a count indicating the number of instances, and a list of properties.
    /// </remarks>
    public class PlyElement
    {
        public string Name { get; }
        public int Count { get; }
        public List<PlyProperty> Properties { get; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="PlyElement"/> class.
        /// </summary>
        /// <param name="name">The name of the PLY element (e.g., "vertex").</param>
        /// <param name="count">The number of instances of this element in the PLY file.</param>
        public PlyElement(string name, int count) { (Name, Count) = (name, count); }
    }
}