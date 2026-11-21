using System;
using System.Collections.Generic;
using System.Linq;
using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class QrCodeMatrixTests
{
    private const string URL = "http://192.168.0.10:5000/?pair=K7QH2MZP";
    private const int QUIET_ZONE = 4;
    private const int FINDER_SIZE = 7;

    [Fact]
    public void Encode_ProducesASquareMatrixWithAQuietZone()
    {
        // Act
        QrCodeMatrix matrix = QrCodeMatrix.Encode(URL);

        // Assert
        int symbolSize = matrix.Size - (2 * QUIET_ZONE);
        ((symbolSize - 17) % 4).Should().Be(0, "QR symbols are 17 + 4 * version modules wide");
        for (int i = 0; i < matrix.Size; i++)
        {
            matrix.IsDark(0, i).Should().BeFalse();
            matrix.IsDark(i, 0).Should().BeFalse();
            matrix.IsDark(matrix.Size - 1, i).Should().BeFalse();
        }
    }

    [Fact]
    public void Encode_DrawsTheTopLeftFinderPattern()
    {
        // Act
        QrCodeMatrix matrix = QrCodeMatrix.Encode(URL);

        // Assert: the outer ring of the 7x7 finder is dark and the ring inside it is light.
        for (int i = 0; i < FINDER_SIZE; i++)
        {
            matrix.IsDark(QUIET_ZONE, QUIET_ZONE + i).Should().BeTrue();
            matrix.IsDark(QUIET_ZONE + FINDER_SIZE - 1, QUIET_ZONE + i).Should().BeTrue();
        }
        matrix.IsDark(QUIET_ZONE + 1, QUIET_ZONE + 1).Should().BeFalse();
        matrix.IsDark(QUIET_ZONE + 3, QUIET_ZONE + 3).Should().BeTrue();
    }

    [Fact]
    public void GetDarkRuns_CoversExactlyTheDarkModules()
    {
        // Arrange
        QrCodeMatrix matrix = QrCodeMatrix.Encode(URL);

        // Act
        IReadOnlyList<QrModuleRun> runs = matrix.GetDarkRuns();

        // Assert
        HashSet<(int Row, int Column)> covered = runs
            .SelectMany(run => Enumerable.Range(run.Column, run.Length).Select(column => (run.Row, column)))
            .ToHashSet();
        int darkCount = 0;
        for (int row = 0; row < matrix.Size; row++)
        {
            for (int column = 0; column < matrix.Size; column++)
            {
                if (matrix.IsDark(row, column))
                {
                    darkCount++;
                    covered.Should().Contain((row, column));
                }
            }
        }
        covered.Should().HaveCount(darkCount);
        runs.Count.Should().BeLessThan(darkCount, "adjacent dark modules merge into one run");
    }

    [Fact]
    public void Encode_RejectsEmptyText()
    {
        // Act
        Action act = () => QrCodeMatrix.Encode(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
