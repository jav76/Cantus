using System;
using System.Collections;
using System.Collections.Generic;
using QRCoder;

namespace Cantus.Client.Models;

/// <summary>A horizontal stretch of dark modules in one row of a QR code.</summary>
public readonly record struct QrModuleRun(int Row, int Column, int Length);

/// <summary>
/// The modules of an encoded QR code, quiet zone included, so a view can draw
/// it as vector shapes without a bitmap renderer.
/// </summary>
public sealed class QrCodeMatrix
{
    private readonly bool[,] _modules;

    private QrCodeMatrix(bool[,] modules)
    {
        _modules = modules;
    }

    /// <summary>Modules per side, quiet zone included.</summary>
    public int Size => _modules.GetLength(0);

    public bool IsDark(int row, int column) => _modules[row, column];

    /// <summary>Encodes text at error correction level M.</summary>
    public static QrCodeMatrix Encode(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        List<BitArray> rows = data.ModuleMatrix;
        int size = rows.Count;
        bool[,] modules = new bool[size, size];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                modules[row, column] = rows[row][column];
            }
        }

        return new QrCodeMatrix(modules);
    }

    /// <summary>
    /// Every dark module, merged into horizontal runs so a view needs one
    /// shape per run instead of one per module.
    /// </summary>
    public IReadOnlyList<QrModuleRun> GetDarkRuns()
    {
        List<QrModuleRun> runs = new();
        int size = Size;
        for (int row = 0; row < size; row++)
        {
            int column = 0;
            while (column < size)
            {
                if (!_modules[row, column])
                {
                    column++;
                    continue;
                }

                int start = column;
                while (column < size && _modules[row, column])
                {
                    column++;
                }

                runs.Add(new QrModuleRun(row, start, column - start));
            }
        }

        return runs;
    }
}
