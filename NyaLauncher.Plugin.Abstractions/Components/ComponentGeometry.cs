namespace NyaLauncher.Plugin.Abstractions.Components;

/// <summary>A normalized point in a component's local coordinate system.</summary>
public readonly record struct ComponentPoint(double X, double Y);

/// <summary>A device-independent component footprint.</summary>
public readonly record struct ComponentSize(double Width, double Height);

/// <summary>A normalized rectangle inside a component.</summary>
public readonly record struct ComponentRect(double X, double Y, double Width, double Height);

/// <summary>
/// An integer pixel rectangle inside an image source. Coordinates are measured
/// from the source image's top-left corner.
/// </summary>
public readonly record struct ComponentPixelRect(int X, int Y, int Width, int Height);

/// <summary>Device-independent padding used by a component surface.</summary>
public readonly record struct ComponentThickness(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public ComponentThickness(double uniform) : this(uniform, uniform, uniform, uniform)
    {
    }
}

/// <summary>
/// A simple polygon expressed using normalized coordinates. Both convex and
/// concave polygons are supported; holes and self-intersections are not.
/// </summary>
public sealed class PolygonShapeDefinition
{
    public required IReadOnlyList<ComponentPoint> Points { get; init; }

    public static PolygonShapeDefinition Rectangle() => FromPoints(
        new(0, 0),
        new(1, 0),
        new(1, 1),
        new(0, 1));

    public static PolygonShapeDefinition CutCorner(double inset = 0.12)
    {
        if (!double.IsFinite(inset))
            throw new ArgumentOutOfRangeException(nameof(inset), "切角尺寸必须是有限数值。");

        var value = Math.Clamp(inset, 0.01, 0.49);
        return FromPoints(
            new(value, 0),
            new(1 - value, 0),
            new(1, value),
            new(1, 1 - value),
            new(1 - value, 1),
            new(value, 1),
            new(0, 1 - value),
            new(0, value));
    }

    public static PolygonShapeDefinition RegularPolygon(
        int sides,
        double rotationDegrees = -90,
        double radius = 0.5)
    {
        if (sides is < 3 or > 64)
            throw new ArgumentOutOfRangeException(nameof(sides), "边数必须介于 3 与 64 之间。");
        if (!double.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees), "旋转角度必须是有限数值。");
        if (!double.IsFinite(radius) || radius <= 0 || radius > 0.5)
            throw new ArgumentOutOfRangeException(nameof(radius), "半径必须是 (0, 0.5] 范围内的有限数值。");

        var rotation = Math.IEEERemainder(rotationDegrees, 360) * Math.PI / 180;
        var points = new ComponentPoint[sides];
        for (var index = 0; index < sides; index++)
        {
            var angle = rotation + (Math.PI * 2 * index / sides);
            points[index] = new ComponentPoint(
                0.5 + Math.Cos(angle) * radius,
                0.5 + Math.Sin(angle) * radius);
        }

        return new PolygonShapeDefinition { Points = Array.AsReadOnly(points) };
    }

    public static PolygonShapeDefinition FromPoints(params ComponentPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new PolygonShapeDefinition
        {
            Points = Array.AsReadOnly(points.ToArray())
        };
    }

    /// <summary>Point-in-polygon test including points that lie on an edge.</summary>
    public bool Contains(ComponentPoint point)
    {
        if (Points.Count < 3 || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return false;

        var inside = false;
        for (int current = 0, previous = Points.Count - 1;
             current < Points.Count;
             previous = current++)
        {
            var a = Points[previous];
            var b = Points[current];
            if (IsPointOnSegment(point, a, b))
                return true;

            var crosses = (a.Y > point.Y) != (b.Y > point.Y) &&
                          point.X < (b.X - a.X) * (point.Y - a.Y) /
                          (b.Y - a.Y) + a.X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private static bool IsPointOnSegment(
        ComponentPoint point,
        ComponentPoint start,
        ComponentPoint end)
    {
        const double epsilon = 0.000000001;
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= epsilon * epsilon)
        {
            var pointDeltaX = point.X - start.X;
            var pointDeltaY = point.Y - start.Y;
            return pointDeltaX * pointDeltaX + pointDeltaY * pointDeltaY <=
                   epsilon * epsilon;
        }

        var cross = (point.Y - start.Y) * (end.X - start.X) -
                    (point.X - start.X) * (end.Y - start.Y);
        if (cross * cross > epsilon * epsilon * lengthSquared)
            return false;

        var dot = (point.X - start.X) * (end.X - start.X) +
                  (point.Y - start.Y) * (end.Y - start.Y);
        var projection = dot / lengthSquared;
        return projection >= -epsilon && projection <= 1 + epsilon;
    }
}
