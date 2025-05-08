# SlimPly

SlimPly is a lightweight and efficient tool for compressing and processing PLY (Polygon File Format) files. It is designed to handle large 3D datasets with advanced features like spatially coherent chunking, spherical harmonic (SH) coefficients for lighting, and binary output in a compressed format.

## Features

- **PLY Compression**: Compresses PLY files into a compact binary format.
- **Spatial Coherence**: Sorts splats in Morton-order (Z-order curve) for efficient processing.
- **Spherical Harmonic Coefficients**: Supports SH coefficients for advanced lighting effects.
- **Optimized Performance**: Processes large datasets with minimal memory usage.
- **Cross-Platform**: Built with .NET 8, ensuring compatibility across platforms.

## Requirements

- **.NET SDK**: Version 8.0 or higher.
- **C# Version**: 12.0 or higher.

## Installation

1. Clone the repository:
git clone https://github.com/your-username/slimply.git cd slimply

2. Build the project using the .NET CLI:
dotnet build

## Usage

Run SlimPly from the command line:
SlimPly <input.ply> <output.compressed.ply>
