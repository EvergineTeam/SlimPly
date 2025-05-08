namespace SlimPly
{
    /// <summary>
    /// Represents a PLY file, which includes the header, elements, and binary payload.
    /// </summary>
    /// <remarks>
    /// A PLY file consists of a header that describes the structure of the data, a list of elements
    /// that define the data organization, and a binary payload containing the actual data.
    /// </remarks>
    public class PlyFile
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PlyFile"/> class.
        /// </summary>
        /// <param name="headerLines">The lines of the PLY file header.</param>
        /// <param name="elements">The list of elements defined in the PLY file.</param>
        /// <param name="data">The binary payload of the PLY file.</param>
        public PlyFile(string[] headerLines, List<PlyElement> elements, byte[] data)
        {
            HeaderLines = headerLines;
            Elements = elements;
            Data = data;
        }

        /// <summary>
        /// Gets the lines of the PLY file header.
        /// </summary>
        public string[] HeaderLines { get; }

        /// <summary>
        /// Gets the list of elements defined in the PLY file.
        /// </summary>
        public List<PlyElement> Elements { get; }

        /// <summary>
        /// Gets the binary payload of the PLY file.
        /// </summary>
        public byte[] Data { get; }
    }
}

