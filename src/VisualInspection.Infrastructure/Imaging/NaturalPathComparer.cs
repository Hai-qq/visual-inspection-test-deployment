namespace VisualInspection.Infrastructure.Imaging;

internal sealed class NaturalPathComparer : IComparer<string>
{
    public static NaturalPathComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < x.Length && rightIndex < y.Length)
        {
            if (char.IsDigit(x[leftIndex]) && char.IsDigit(y[rightIndex]))
            {
                var comparison = CompareNumber(x, ref leftIndex, y, ref rightIndex);
                if (comparison != 0) return comparison;
                continue;
            }

            var left = char.ToUpperInvariant(x[leftIndex]);
            var right = char.ToUpperInvariant(y[rightIndex]);
            if (left != right) return left.CompareTo(right);
            leftIndex++;
            rightIndex++;
        }

        return (x.Length - leftIndex).CompareTo(y.Length - rightIndex);
    }

    private static int CompareNumber(string left, ref int leftIndex, string right, ref int rightIndex)
    {
        var leftStart = leftIndex;
        var rightStart = rightIndex;
        while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
        while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;

        var leftSignificant = leftStart;
        var rightSignificant = rightStart;
        while (leftSignificant < leftIndex && left[leftSignificant] == '0') leftSignificant++;
        while (rightSignificant < rightIndex && right[rightSignificant] == '0') rightSignificant++;

        var leftLength = leftIndex - leftSignificant;
        var rightLength = rightIndex - rightSignificant;
        if (leftLength != rightLength) return leftLength.CompareTo(rightLength);

        for (var offset = 0; offset < leftLength; offset++)
        {
            var comparison = left[leftSignificant + offset].CompareTo(right[rightSignificant + offset]);
            if (comparison != 0) return comparison;
        }

        return (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
    }
}
