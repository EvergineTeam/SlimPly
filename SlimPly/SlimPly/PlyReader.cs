using System.Text;

namespace SlimPly
{
    /// <summary>
    /// Provides functionality to read and parse PLY (Polygon File Format) files.
    /// </summary>
    /// <remarks>
    /// The <see cref="PlyReader"/> class is responsible for reading PLY files, parsing their headers,
    /// and extracting the binary payload. It supports both ASCII and binary PLY formats.
    /// </remarks>
    public static class PlyReader
    {
        private static readonly byte[] _magic = Encoding.ASCII.GetBytes("ply\n");
        private static readonly byte[] _endHeader = Encoding.ASCII.GetBytes("\nend_header\n");
        private const int MaxHeaderBytes = 128 * 1024;

        /// <summary>
        /// Reads and parses a PLY (Polygon File Format) file asynchronously.
        /// </summary>
        /// <param name="filePath">The path to the PLY file to read.</param>
        /// <returns>
        /// A <see cref="PlyFile"/> object containing the parsed header, elements, and binary payload of the PLY file.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// Thrown if the file is not a valid PLY file, the header is malformed, or unexpected EOF is encountered.
        /// </exception>
        /// <exception cref="IOException">Thrown if an I/O error occurs while reading the file.</exception>
        /// <remarks>
        /// This method supports both ASCII and binary PLY formats. It reads the file header to extract metadata
        /// and then reads the binary payload. The header is parsed to identify elements and their properties.
        /// </remarks>
        public static async Task<PlyFile> ReadAsync(string filePath)
        {
            await using var fs = File.OpenRead(filePath);

            // Read header
            var first = new byte[1];
            if (await fs.ReadAsync(first) != 1 || first[0] != (byte)'p')
                throw new InvalidDataException("Missing PLY magic.");

            var header = new List<byte>(MaxHeaderBytes);
            header.Add(first[0]);                        // 'p'

            // read the rest of "ply\n"
            var rest = new byte[_magic.Length - 1];      // 3 bytes: 'l','y','\n'
            await fs.ReadExactlyAsync(rest);
            header.AddRange(rest);

            // keep reading header one byte at a time …
            var tmp = new byte[1];
            while (header.Count < MaxHeaderBytes)
            {
                if (await fs.ReadAsync(tmp) != 1)
                    throw new InvalidDataException("Unexpected EOF while reading header.");

                header.Add(tmp[0]);
                if (EndsWith(header, _endHeader)) break;
            }

            var headerText = Encoding.ASCII.GetString(header.ToArray());
            var headerLines = headerText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Parse header
            var elements = new List<PlyElement>();
            PlyElement? current = null;

            foreach (var rawLine in headerLines[1..])        // skip “ply”
            {
                var line = rawLine.Trim();
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "format":
                    case "comment":
                    case "end_header":
                        break;

                    case "element":
                        if (parts.Length != 3)
                            throw new InvalidDataException("Malformed element line.");
                        current = new PlyElement(parts[1], int.Parse(parts[2]));
                        elements.Add(current);
                        break;

                    case "property":
                        if (current is null || parts.Length != 3)
                            throw new InvalidDataException("property outside element.");
                        current.Properties.Add(new PlyProperty(parts[2], parts[1]));
                        break;

                    default:
                        throw new InvalidDataException($"Unknown header token '{parts[0]}'.");
                }
            }

            // Read binary payload
            var dataBytes = (int)(fs.Length - fs.Position);
            var data = new byte[dataBytes];
            await fs.ReadExactlyAsync(data);

            return new PlyFile(headerLines, elements, data);
        }

        /// <summary>
        /// Checks if the end of a list of bytes matches a specified pattern.
        /// </summary>
        /// <param name="list">The list of bytes to check.</param>
        /// <param name="pattern">The byte pattern to compare against the end of the list.</param>
        /// <returns>
        /// <c>true</c> if the end of the list matches the specified pattern; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This method is used to verify if a sequence of bytes ends with a specific pattern, such as
        /// detecting the end of a header in a PLY file.
        /// </remarks>
        private static bool EndsWith(IReadOnlyList<byte> list, byte[] pattern)
        {
            if (list.Count < pattern.Length)
                return false;

            int start = list.Count - pattern.Length;

            for (int i = 0; i < pattern.Length; ++i)
                if (list[start + i] != pattern[i])
                    return false;

            return true;
        }
    }
}
