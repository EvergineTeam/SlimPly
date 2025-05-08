namespace SlimPly
{
    /// <summary>
    /// Represents a Splat, which is a wrapper around a PLY file's vertex data.
    /// </summary>
    /// <remarks>
    /// The Splat class provides functionality to read and interpret vertex data from a PLY file.
    /// It supports accessing specific fields, calculating the number of spherical harmonics (SH) bands,
    /// and interpreting binary payloads as floating-point data.
    /// </remarks>
    public class Splat
    {
        private readonly PlyFile f;
        private readonly PlyElement vtx;
        private readonly Dictionary<string, (int ofs, int size)> ofs = [];
        private readonly float[] _data; // float-view of the whole binary payload

        /// <summary>
        /// Initializes a new instance of the <see cref="Splat"/> class.
        /// </summary>
        /// <param name="file">The PLY file containing vertex data to be wrapped by this instance.</param>
        /// <remarks>
        /// This constructor processes the provided PLY file to extract vertex data and prepare it for efficient access.
        /// It calculates the offsets and sizes of each property in the vertex data, and creates a float array
        /// representation of the binary payload for fast field access.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the PLY file does not contain a "vertex" element.
        /// </exception>
        public Splat(PlyFile file)
        {
            f = file;
            vtx = f.Elements.First(e => e.Name == "vertex");

            var strideBytes = 0;
            foreach (var p in vtx.Properties)
            {
                ofs[p.Name] = (strideBytes / 4, SizeOf(p.Type));
                strideBytes += SizeOf(p.Type);
            }

            _data = new float[f.Data.Length / 4];
            Buffer.BlockCopy(f.Data, 0, _data, 0, f.Data.Length);
        }

        public int NumSplats => vtx.Count;

        /// <summary>
        /// Gets the number of extra spherical harmonics (SH) bands present in the vertex data.
        /// </summary>
        /// <remarks>
        /// The number of SH bands is determined by the count of fields in the vertex data
        /// that start with the prefix "f_rest_". The mapping is as follows:
        /// <list type="bullet">
        /// <item>0 fields: 0 bands (DC only)</item>
        /// <item>1 to 23 fields: 1 band (+L1)</item>
        /// <item>24 to 44 fields: 2 bands (+L2)</item>
        /// <item>45 or more fields: 3 bands (+L3)</item>
        /// </list>
        /// </remarks>
        public int NumSHBands =>
            ofs.Keys.Count(k => k.StartsWith("f_rest_")) switch
            {
                0 => 0,
                >= 45 => 3,
                >= 24 => 2,
                _ => 1
            };

        /// <summary>
        /// Reads specific fields from the vertex data of a PLY file for a given index.
        /// </summary>
        /// <param name="i">The index of the vertex to read.</param>
        /// <param name="fields">An array of field names to extract from the vertex data.</param>
        /// <param name="dst">
        /// A span of floats where the extracted field values will be stored. 
        /// The length of the span must match the number of fields specified.
        /// </param>
        /// <remarks>
        /// This method reads the binary payload of the PLY file and extracts the specified fields
        /// for the vertex at the given index. The extracted values are written into the provided span.
        /// </remarks>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if any of the specified fields do not exist in the vertex data.
        /// </exception>
        /// <exception cref="IndexOutOfRangeException">
        /// Thrown if the index is out of range for the vertex data.
        /// </exception>
        public void Read(int i, string[] fields, Span<float> dst)
        {
            var strideFloats = ofs.Count;     // one float per property (PLY spec says float32)
            var baseIdx = i * strideFloats;
            for (var j = 0; j < fields.Length; ++j)
            {
                var (ofs, _) = this.ofs[fields[j]];
                dst[j] = _data[baseIdx + ofs];
            }
        }

        /// <summary>
        /// Determines the size in bytes of a given PLY data type.
        /// </summary>
        /// <param name="plyType">The PLY data type as a string (e.g., "char", "float", "double").</param>
        /// <returns>The size in bytes of the specified PLY data type.</returns>
        /// <exception cref="NotSupportedException">
        /// Thrown if the provided PLY data type is not recognized or supported.
        /// </exception>
        /// <remarks>
        /// This method maps PLY data types to their corresponding sizes in bytes:
        /// <list type="bullet">
        /// <item><description>"char" or "uchar": 1 byte</description></item>
        /// <item><description>"short" or "ushort": 2 bytes</description></item>
        /// <item><description>"int", "uint", or "float": 4 bytes</description></item>
        /// <item><description>"double": 8 bytes</description></item>
        /// </list>
        /// </remarks>
        public static int SizeOf(string plyType) =>
            plyType switch
            {
                "char" or "uchar" => 1,
                "short" or "ushort" => 2,
                "int" or "uint" or "float" => 4,
                "double" => 8,
                _ => throw new NotSupportedException($"Unknown PLY type '{plyType}'.")
            };
    }
}