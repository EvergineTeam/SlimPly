using System.Numerics;
using System.Text;

namespace SlimPly
{
    /// <summary>
    /// Provides functionality to write compressed PLY files asynchronously.
    /// </summary>
    /// <remarks>
    /// The <see cref="CompressedPlyWriter"/> class is designed to handle the efficient processing and writing of PLY files
    /// with compressed vertex data. It supports spatially coherent chunking, spherical harmonic (SH) coefficients for advanced
    /// lighting effects, and binary little-endian output format. The class includes methods for sorting splats in Morton-order,
    /// packing vertex data, and building PLY headers.
    /// 
    /// Key features:
    /// - Supports optional SH coefficients for lighting.
    /// - Processes splats into spatially coherent chunks.
    /// - Writes binary PLY files with a custom header.
    /// - Optimized for performance and memory usage.
    /// </remarks>
    public static class CompressedPlyWriter
    {
        private static readonly int[] shCoeffTable = { 0, 3, 8, 15 };

        /// <summary>
        /// Writes a compressed PLY file asynchronously using the provided splat data.
        /// </summary>
        /// <param name="filePath">The file path where the PLY file will be written.</param>
        /// <param name="splat">The <see cref="Splat"/> object containing the data to be written.</param>
        /// <returns>A task representing the asynchronous write operation.</returns>
        /// <remarks>
        /// This method processes the splat data into spatially coherent chunks, compresses the data,
        /// and writes it to a binary PLY file. The PLY file includes a header, chunk metadata, vertex data,
        /// and optional spherical harmonic (SH) coefficients.
        /// 
        /// The method performs the following steps:
        /// 1. Allocates memory for chunk metadata, vertex data, and SH coefficients.
        /// 2. Sorts the splats in Morton-order for spatial coherence.
        /// 3. Processes each chunk to normalize and pack the data.
        /// 4. Writes the PLY header and binary data to the specified file.
        /// 
        /// The output file is in binary little-endian format and supports optional SH coefficients
        /// for advanced lighting effects.
        /// </remarks>
        public static async Task WriteAsync(string filePath, Splat splat)
        {
            // Constants & allocations
            int numSplats = splat.NumSplats;
            int numChunks = (numSplats + 255) / 256;
            int shBands = splat.NumSHBands;              // 0–3
            int shCoeffsRGB = shCoeffTable[shBands] * 3;    // uchar count per splat

            var chunkFloat = new float[numChunks * 18];      // 18 floats / chunk
            var vertexUint = new uint[numSplats * 4];       // 4 uint32 / vertex
            var shBytes = new byte[numSplats * shCoeffsRGB];

            // Morton-order the splats so chunks are spatially coherent
            var indices = Enumerable.Range(0, numSplats).ToArray();
            MortonSort(splat, indices);

            // Process one chunk at a time
            var baseFields = new[] {
            "x","y","z",
            "scale_0","scale_1","scale_2",
            "f_dc_0","f_dc_1","f_dc_2",
            "opacity",
            "rot_0","rot_1","rot_2","rot_3"
        };
            var shFields = Enumerable.Range(0, shCoeffsRGB)
                                     .Select(i => $"f_rest_{i}")
                                     .ToArray();

            var tmpBase = new float[baseFields.Length];
            var tmpSH = new float[shCoeffsRGB];

            const float SH_C0 = 0.28209479177387814f;
            const float SH_NORM = 1f / 8f;   //  = 0.125, matches TypeScript version

            for (int c = 0; c < numChunks; ++c)
            {
                int chunkStart = c * 256;
                int count = Math.Min(256, numSplats - chunkStart);

                // Gather & preprocess values
                var x = new float[count]; var y = new float[count]; var z = new float[count];
                var sx = new float[count]; var sy = new float[count]; var sz = new float[count];
                var r = new float[count]; var g = new float[count]; var b = new float[count];
                var op = new float[count];
                var qx = new float[count]; var qy = new float[count]; var qz = new float[count]; var qw = new float[count];

                for (int j = 0; j < count; ++j)
                {
                    int global = indices[chunkStart + j];

                    splat.Read(global, baseFields, tmpBase);
                    splat.Read(global, shFields, tmpSH);

                    // positions & scales
                    x[j] = tmpBase[0]; y[j] = tmpBase[1]; z[j] = tmpBase[2];
                    sx[j] = Clamp(tmpBase[3], -20, 20);
                    sy[j] = Clamp(tmpBase[4], -20, 20);
                    sz[j] = Clamp(tmpBase[5], -20, 20);

                    // DC colour & opacity
                    r[j] = tmpBase[6] * SH_C0 + 0.5f;
                    g[j] = tmpBase[7] * SH_C0 + 0.5f;
                    b[j] = tmpBase[8] * SH_C0 + 0.5f;
                    op[j] = 1f / (1f + MathF.Exp(-tmpBase[9]));

                    // quaternion
                    qx[j] = tmpBase[10]; qy[j] = tmpBase[11];
                    qz[j] = tmpBase[12]; qw[j] = tmpBase[13];

                    // SH coefficients → 8-bit
                    int shBase = (chunkStart + j) * shCoeffsRGB;
                    for (int k = 0; k < shCoeffsRGB; ++k)
                    {
                        float n = tmpSH[k] * SH_NORM + 0.5f;          // [-8,8] → [-0.5,1.5]
                        int u = (int)(n * 256f);
                        shBytes[shBase + k] = (byte)Math.Clamp(u, 0, 255);
                    }
                }

                // Pack into vertexUint & chunkFloat
                PackChunk(
                    chunkIndex: c,
                    destStart: chunkStart,
                    count,
                    x, y, z,
                    sx, sy, sz,
                    r, g, b,
                    op,
                    qx, qy, qz, qw,
                    chunkFloat,
                    vertexUint
                );
            }

            // Write the PLY
            await using var fs = File.Create(filePath);
            await using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

            bw.Write(Encoding.ASCII.GetBytes(
                BuildHeader(numChunks, numSplats, shBands, shCoeffsRGB)
            ));

            foreach (var f in chunkFloat) bw.Write(f);
            foreach (var u in vertexUint) bw.Write(u);
            if (shBands > 0) bw.Write(shBytes);

            await fs.FlushAsync();
        }

        /// <summary>
        /// Sorts the given <paramref name="indices"/> array in Morton-order (3-D Z-curve) based on the spatial positions
        /// of the splats in the provided <paramref name="splat"/> object.
        /// </summary>
        /// <param name="splat">The <see cref="Splat"/> object containing the spatial data of the splats.</param>
        /// <param name="indices">An array of indices representing the splats to be sorted.</param>
        /// <remarks>
        /// This method calculates Morton codes for each splat based on their normalized 3D positions
        /// and performs a stable sort of the indices array using these Morton codes. The Morton-order
        /// ensures spatial coherence, which is beneficial for chunk-based processing.
        /// </remarks>
        private static void MortonSort(Splat splat, int[] indices)
        {
            int n = indices.Length;
            var xyz = new float[3];

            // --- scene bounds --------------------------------------------------
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < n; ++i)
            {
                splat.Read(i, ["x", "y", "z"], xyz);

                float x = xyz[0], y = xyz[1], z = xyz[2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }

            float lenX = maxX - minX; if (lenX == 0) lenX = 1;
            float lenY = maxY - minY; if (lenY == 0) lenY = 1;
            float lenZ = maxZ - minZ; if (lenZ == 0) lenZ = 1;

            // Morton codes
            var morton = new uint[n];
            for (int i = 0; i < n; ++i)
            {
                splat.Read(i, ["x", "y", "z"], xyz);

                int ix = (int)(1024 * (xyz[0] - minX) / lenX);
                int iy = (int)(1024 * (xyz[1] - minY) / lenY);
                int iz = (int)(1024 * (xyz[2] - minZ) / lenZ);

                morton[i] = Morton3(ix, iy, iz);
            }

            // Stable sort indices by morton code
            Array.Sort(indices, (a, b) => morton[a].CompareTo(morton[b]));
        }

        /// <summary>
        /// Packs a single 256-splat chunk into the <paramref name="vertexUint"/> array and writes the
        /// minimum and maximum values of the chunk's attributes to the <paramref name="chunkFloat"/> array.
        /// </summary>
        /// <param name="chunkIndex">The index of the current chunk being processed.</param>
        /// <param name="destStart">The starting index in the <paramref name="vertexUint"/> array for this chunk.</param>
        /// <param name="count">The number of splats in the current chunk.</param>
        /// <param name="x">The array of x-coordinates for the splats in the chunk.</param>
        /// <param name="y">The array of y-coordinates for the splats in the chunk.</param>
        /// <param name="z">The array of z-coordinates for the splats in the chunk.</param>
        /// <param name="sx">The array of x-scale values for the splats in the chunk.</param>
        /// <param name="sy">The array of y-scale values for the splats in the chunk.</param>
        /// <param name="sz">The array of z-scale values for the splats in the chunk.</param>
        /// <param name="r">The array of red color values for the splats in the chunk.</param>
        /// <param name="g">The array of green color values for the splats in the chunk.</param>
        /// <param name="b">The array of blue color values for the splats in the chunk.</param>
        /// <param name="op">The array of opacity values for the splats in the chunk.</param>
        /// <param name="qx">The array of x-components of the quaternion rotations for the splats.</param>
        /// <param name="qy">The array of y-components of the quaternion rotations for the splats.</param>
        /// <param name="qz">The array of z-components of the quaternion rotations for the splats.</param>
        /// <param name="qw">The array of w-components of the quaternion rotations for the splats.</param>
        /// <param name="chunkFloat">The array to store the minimum and maximum values of the chunk's attributes.</param>
        /// <param name="vertexUint">The array to store the packed vertex data for the splats.</param>
        /// <remarks>
        /// This method normalizes the input data for each splat, packs it into a compact format, and writes
        /// it to the <paramref name="vertexUint"/> array. It also calculates the minimum and maximum values
        /// for each attribute in the chunk and writes them to the <paramref name="chunkFloat"/> array.
        /// </remarks>
        private static void PackChunk(
            int chunkIndex,
            int destStart,
            int count,
            float[] x, float[] y, float[] z,
            float[] sx, float[] sy, float[] sz,
            float[] r, float[] g, float[] b,
            float[] op,
            float[] qx, float[] qy, float[] qz, float[] qw,
            float[] chunkFloat,
            uint[] vertexUint)
        {
            //--------------------------------------------------------------------
            //  min/max helpers
            //--------------------------------------------------------------------
            static (float min, float max) MinMax(ReadOnlySpan<float> arr)
            {
                float mn = arr[0], mx = arr[0];
                foreach (var v in arr)
                { if (v < mn) mn = v; if (v > mx) mx = v; }
                return (mn, mx);
            }
            static float Norm(float v, float mn, float mx)
                => mx - mn < 1e-5f ? 0f : (v - mn) / (mx - mn);

            var (mnx, mxx) = MinMax(x); var (mny, mxy) = MinMax(y); var (mnz, mxz) = MinMax(z);
            var (mnsx, mxsx) = MinMax(sx); var (mnsy, mxsy) = MinMax(sy); var (mnsz, mxsz) = MinMax(sz);
            var (mnr, mxr) = MinMax(r); var (mng, mxg) = MinMax(g); var (mnb, mxb) = MinMax(b);

            //--------------------------------------------------------------------
            //  per-splat packing
            //--------------------------------------------------------------------
            for (int j = 0; j < count; ++j)
            {
                int outIdx = (destStart + j) * 4;

                vertexUint[outIdx + 0] = Pack111011(
                    Norm(x[j], mnx, mxx),
                    Norm(y[j], mny, mxy),
                    Norm(z[j], mnz, mxz));

                vertexUint[outIdx + 1] = PackQuaternion(qx[j], qy[j], qz[j], qw[j]);

                vertexUint[outIdx + 2] = Pack111011(
                    Norm(sx[j], mnsx, mxsx),
                    Norm(sy[j], mnsy, mxsy),
                    Norm(sz[j], mnsz, mxsz));

                vertexUint[outIdx + 3] = Pack8888(
                    Norm(r[j], mnr, mxr),
                    Norm(g[j], mng, mxg),
                    Norm(b[j], mnb, mxb),
                    op[j]);
            }

            // Write chunk’s min/max block
            int cBase = chunkIndex * 18;

            chunkFloat[cBase + 0] = mnx; chunkFloat[cBase + 1] = mny; chunkFloat[cBase + 2] = mnz;
            chunkFloat[cBase + 3] = mxx; chunkFloat[cBase + 4] = mxy; chunkFloat[cBase + 5] = mxz;

            chunkFloat[cBase + 6] = mnsx; chunkFloat[cBase + 7] = mnsy; chunkFloat[cBase + 8] = mnsz;
            chunkFloat[cBase + 9] = mxsx; chunkFloat[cBase + 10] = mxsy; chunkFloat[cBase + 11] = mxsz;

            chunkFloat[cBase + 12] = mnr; chunkFloat[cBase + 13] = mng; chunkFloat[cBase + 14] = mnb;
            chunkFloat[cBase + 15] = mxr; chunkFloat[cBase + 16] = mxg; chunkFloat[cBase + 17] = mxb;
        }

        /// <summary>
        /// Builds the ASCII PLY header string for the compressed format.
        /// </summary>
        /// <param name="numChunks">The number of chunks in the PLY file.</param>
        /// <param name="numSplats">The total number of splats (vertices) in the PLY file.</param>
        /// <param name="shBands">The number of spherical harmonic (SH) bands used (0–3).</param>
        /// <param name="shCoeffsRGB">The number of SH coefficients per splat for RGB channels.</param>
        /// <returns>A string representing the ASCII PLY header.</returns>
        /// <remarks>
        /// The header includes metadata about the PLY file format, the number of elements (chunks, vertices, and SH coefficients),
        /// and the properties of each element. It is designed for binary little-endian PLY files and supports optional SH coefficients.
        /// </remarks>
        private static string BuildHeader(int numChunks, int numSplats, int shBands, int shCoeffsRGB)
        {
            var sb = new StringBuilder()
                   .AppendLine("ply")
                   .AppendLine("format binary_little_endian 1.0")
                   .AppendLine("comment Generated by C# splat-transform")
                   .AppendLine($"element chunk {numChunks}")
                   .AppendJoin('\n', new[]
                   {
            "min_x","min_y","min_z","max_x","max_y","max_z",
            "min_scale_x","min_scale_y","min_scale_z",
            "max_scale_x","max_scale_y","max_scale_z",
            "min_r","min_g","min_b","max_r","max_g","max_b"
                   }.Select(p => $"property float {p}")).Append('\n')
                   .AppendLine($"element vertex {numSplats}")
                   .AppendLine("property uint packed_position")
                   .AppendLine("property uint packed_rotation")
                   .AppendLine("property uint packed_scale")
                   .AppendLine("property uint packed_color");

            if (shBands > 0)
            {
                sb.AppendLine($"element sh {numSplats}");
                for (int i = 0; i < shCoeffsRGB; ++i)
                    sb.AppendLine($"property uchar f_rest_{i}");
            }

            sb.AppendLine("end_header");

            return sb.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Clamps a given value between a specified minimum and maximum range.
        /// </summary>
        /// <param name="v">The value to clamp.</param>
        /// <param name="mn">The minimum allowable value.</param>
        /// <param name="mx">The maximum allowable value.</param>
        /// <returns>The clamped value, constrained between <paramref name="mn"/> and <paramref name="mx"/>.</returns>
        private static float Clamp(float v, float mn, float mx) => Math.Clamp(v, mn, mx);

        /// <summary>
        /// Packs a normalized floating-point value into an unsigned integer with a specified number of bits.
        /// </summary>
        /// <param name="v">The normalized floating-point value to pack, expected to be in the range [0, 1].</param>
        /// <param name="bits">The number of bits to use for packing the value.</param>
        /// <returns>
        /// An unsigned integer representation of the normalized value, scaled to fit within the specified bit range.
        /// </returns>
        /// <remarks>
        /// This method ensures that the input value is clamped to the range [0, 1] before packing.
        /// The packed value is calculated by scaling the input to the range of the target bit size
        /// and rounding to the nearest integer.
        /// </remarks>
        private static uint PackUnorm(float v, int bits)
        {
            var t = (1u << bits) - 1u;
            var f = Math.Clamp(v, 0f, 1f);
            return (uint)MathF.Round(f * t);
        }

        /// <summary>
        /// Packs three normalized floating-point values (nx, ny, nz) into a single 32-bit unsigned integer.
        /// </summary>
        /// <param name="nx">The normalized x-coordinate, expected to be in the range [0, 1].</param>
        /// <param name="ny">The normalized y-coordinate, expected to be in the range [0, 1].</param>
        /// <param name="nz">The normalized z-coordinate, expected to be in the range [0, 1].</param>
        /// <returns>
        /// A 32-bit unsigned integer where:
        /// - The first 11 bits represent the packed x-coordinate.
        /// - The next 10 bits represent the packed y-coordinate.
        /// - The last 11 bits represent the packed z-coordinate.
        /// </returns>
        /// <remarks>
        /// This method uses a fixed bit allocation of 11 bits for x, 10 bits for y, and 11 bits for z.
        /// The input values are clamped to the range [0, 1] before packing.
        /// </remarks>
        private static uint Pack111011(float nx, float ny, float nz)
            => (PackUnorm(nx, 11) << 21)
             | (PackUnorm(ny, 10) << 11)
             | PackUnorm(nz, 11);

        /// <summary>
        /// Packs four normalized floating-point values (r, g, b, a) into a single 32-bit unsigned integer.
        /// </summary>
        /// <param name="r">The normalized red channel value, expected to be in the range [0, 1].</param>
        /// <param name="g">The normalized green channel value, expected to be in the range [0, 1].</param>
        /// <param name="b">The normalized blue channel value, expected to be in the range [0, 1].</param>
        /// <param name="a">The normalized alpha channel value, expected to be in the range [0, 1].</param>
        /// <returns>
        /// A 32-bit unsigned integer where:
        /// - The first 8 bits represent the packed red channel.
        /// - The next 8 bits represent the packed green channel.
        /// - The next 8 bits represent the packed blue channel.
        /// - The last 8 bits represent the packed alpha channel.
        /// </returns>
        /// <remarks>
        /// This method ensures that each input value is clamped to the range [0, 1] before packing.
        /// The packed value is calculated by scaling each input to fit within 8 bits and combining them
        /// into a single 32-bit unsigned integer.
        /// </remarks>
        private static uint Pack8888(float r, float g, float b, float a)
            => (PackUnorm(r, 8) << 24)
             | (PackUnorm(g, 8) << 16)
             | (PackUnorm(b, 8) << 8)
             | PackUnorm(a, 8);

        /// <summary>
        /// Packs a quaternion (x, y, z, w) into a 32-bit unsigned integer representation.
        /// </summary>
        /// <param name="x">The x-component of the quaternion.</param>
        /// <param name="y">The y-component of the quaternion.</param>
        /// <param name="z">The z-component of the quaternion.</param>
        /// <param name="w">The w-component of the quaternion.</param>
        /// <returns>
        /// A 32-bit unsigned integer where:
        /// - The first 2 bits represent the index of the largest component of the quaternion.
        /// - The remaining 30 bits store the other three components, normalized and packed into 10 bits each.
        /// </returns>
        /// <remarks>
        /// This method normalizes the quaternion to ensure it has unit length. The largest component is identified
        /// and excluded from storage, as it can be reconstructed later. The remaining three components are scaled
        /// and packed into 10 bits each, with a bias to ensure positive values.
        /// </remarks>
        private static uint PackQuaternion(float x, float y, float z, float w)
        {
            var q = Quaternion.Normalize(new Quaternion(x, y, z, w));

            var comps = new[] { q.X, q.Y, q.Z, q.W };
            var largest = Enumerable.Range(0, 4)
                                    .Aggregate((m, i) => MathF.Abs(comps[i]) > MathF.Abs(comps[m]) ? i : m);

            if (comps[largest] < 0)
                for (var i = 0; i < 4; ++i) comps[i] = -comps[i];

            const float norm = 0.70710678f; // √2 / 2
            uint res = (uint)largest;
            for (var i = 0; i < 4; ++i)
                if (i != largest)
                    res = (res << 10) | PackUnorm(comps[i] * norm + 0.5f, 10);

            return res;
        }

        /// <summary>
        /// Computes a 3D Morton code (Z-order curve) for the given integer coordinates.
        /// </summary>
        /// <param name="x">The x-coordinate, expected to be a non-negative integer.</param>
        /// <param name="y">The y-coordinate, expected to be a non-negative integer.</param>
        /// <param name="z">The z-coordinate, expected to be a non-negative integer.</param>
        /// <returns>
        /// A 32-bit unsigned integer representing the Morton code for the given coordinates.
        /// The Morton code interleaves the bits of the x, y, and z coordinates to create a
        /// spatially coherent ordering.
        /// </returns>
        /// <remarks>
        /// This method uses bit manipulation to interleave the bits of the x, y, and z
        /// coordinates. It is commonly used in spatial data structures to improve cache
        /// coherence and performance when processing 3D data.
        /// </remarks>
        private static uint Morton3(int x, int y, int z)
        {
            static uint Part1By2(uint v)
            {
                v &= 0x000003ffu;
                v = (v | v << 16) & 0xff0000ffu;
                v = (v | v << 8) & 0x0300f00fu;
                v = (v | v << 4) & 0x030c30c3u;
                v = (v | v << 2) & 0x09249249u;
                return v;
            }
            return (Part1By2((uint)z) << 2) | (Part1By2((uint)y) << 1) | Part1By2((uint)x);
        }
    }
}
