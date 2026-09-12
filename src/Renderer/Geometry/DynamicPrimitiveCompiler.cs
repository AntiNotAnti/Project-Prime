using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// CPU geometry for the dynamic primitives that historically used
    /// the historical immediate-mode renderer. The order below is intentionally the order in the
    /// compatibility renderer, including DS quad-strip winding and the
    /// reversed cylinder cap. Both legacy upload adapters and SDL use this
    /// representation so primitive parity is not hidden in one API backend.
    /// </summary>
    public static class DynamicPrimitiveCompiler
    {
        public static CpuMesh Compile(DrawSubmission submission)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            return submission.Primitive switch
            {
                RenderPrimitive.Box => Box(submission.Points),
                RenderPrimitive.Cylinder => Cylinder(submission.Points),
                RenderPrimitive.Sphere => Sphere(submission.Points),
                RenderPrimitive.Quad => Quad(submission.Points),
                RenderPrimitive.Ngon => Ngon(submission.Points, submission.ItemCount),
                RenderPrimitive.Particle => TexturedQuad(submission.Points, submission.ScaleS, submission.ScaleT),
                RenderPrimitive.TrailSingle => Strip(submission.Points, 0, 4, texcoord: true),
                RenderPrimitive.TrailMulti => TrailMulti(submission),
                RenderPrimitive.TrailStack => TrailStack(submission),
                _ => throw new ArgumentOutOfRangeException(nameof(submission), submission.Primitive,
                    "A dynamic primitive is required.")
            };
        }

        private static RenderVertex Vertex(Vector3 position, Vector2 texcoord = default)
            => new(position, Vector4.One, Vector3.UnitZ, texcoord);

        private static CpuMesh Box(Vector3[] points)
        {
            Require(points, 8);
            var vertices = new RenderVertex[8];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Vertex(points[i]);
            var triangles = new List<int>();
            AppendStrip(triangles, new[] { 2, 6, 0, 4, 1, 5, 3, 7, 2, 6 });
            AppendStrip(triangles, new[] { 5, 4, 7, 6 });
            AppendStrip(triangles, new[] { 3, 2, 1, 0 });
            return new CpuMesh(vertices, triangles.ToArray());
        }

        private static CpuMesh Cylinder(Vector3[] points)
        {
            Require(points, 34);
            var vertices = new RenderVertex[points.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Vertex(points[i]);
            var triangles = new List<int>(16 * 3 * 4);
            AppendFan(triangles, 32, 0, 16);
            // The legacy top cap is deliberately emitted in reverse perimeter
            // order (31 through 16, then 31), preserving front-face winding.
            var top = new int[18];
            top[0] = 33;
            for (int i = 0; i < 16; i++) top[i + 1] = 31 - i;
            top[17] = 31;
            AppendFan(triangles, top);
            AppendStrip(triangles, BuildRangePair(0, 16, 16));
            return new CpuMesh(vertices, triangles.ToArray());
        }

        private static CpuMesh Sphere(Vector3[] points)
        {
            int expected = (16 + 1) * (24 + 1);
            Require(points, expected);
            var vertices = new RenderVertex[expected];
            for (int i = 0; i < expected; i++) vertices[i] = Vertex(points[i]);
            var triangles = new List<int>(16 * 24 * 6);
            for (int stack = 0; stack < 16; stack++)
            {
                int k1 = stack * 25;
                int k2 = k1 + 25;
                for (int sector = 0; sector < 24; sector++, k1++, k2++)
                {
                    if (stack != 0)
                    {
                        triangles.Add(k1 + 1); triangles.Add(k2); triangles.Add(k1);
                    }
                    if (stack != 15)
                    {
                        triangles.Add(k2 + 1); triangles.Add(k2); triangles.Add(k1 + 1);
                    }
                }
            }
            return new CpuMesh(vertices, triangles.ToArray());
        }

        private static CpuMesh Strip(Vector3[] points, int start, int count, bool texcoord = false)
        {
            if (texcoord)
            {
                if (points.Length < count * 2) throw new ArgumentException("Interleaved trail is incomplete.");
                var vertices = new RenderVertex[count];
                for (int i = 0; i < count; i++)
                    vertices[i] = Vertex(points[(start + i) * 2 + 1],
                        new Vector2(points[(start + i) * 2].X, points[(start + i) * 2].Y));
                var triangles = new List<int>();
                AppendQuadStrip(triangles, Range(count));
                return new CpuMesh(vertices, triangles.ToArray());
            }

            Require(points, start + count);
            var result = new RenderVertex[count];
            for (int i = 0; i < count; i++) result[i] = Vertex(points[start + i]);
            var output = new List<int>();
            AppendStrip(output, Range(count));
            return new CpuMesh(result, output.ToArray());
        }

        private static CpuMesh Ngon(Vector3[] points, int count)
        {
            Require(points, count);
            if (count < 3) return new CpuMesh(Array.Empty<RenderVertex>(), Array.Empty<int>());
            var vertices = new RenderVertex[count];
            for (int i = 0; i < count; i++) vertices[i] = Vertex(points[i]);
            var triangles = new List<int>((count - 2) * 3);
            for (int i = 1; i < count - 1; i++)
            {
                triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
            }
            var lines = new int[count * 2];
            for (int i = 0; i < count; i++)
            {
                lines[i * 2] = i;
                lines[i * 2 + 1] = (i + 1) % count;
            }
            return new CpuMesh(vertices, triangles.ToArray(), lines);
        }

        private static CpuMesh TexturedQuad(Vector3[] points, float scaleS, float scaleT)
        {
            Require(points, 8);
            var vertices = new RenderVertex[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 uv = points[i * 2];
                vertices[i] = Vertex(points[i * 2 + 1], new Vector2(uv.X * scaleS, uv.Y * scaleT));
            }
            var triangles = new List<int>();
            AppendQuad(triangles, 0, 1, 2, 3);
            return new CpuMesh(vertices, triangles.ToArray());
        }

        private static CpuMesh Quad(Vector3[] points)
        {
            Require(points, 4);
            var vertices = new RenderVertex[4];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = Vertex(points[i]);
            var triangles = new List<int>(6);
            // RenderQuad's compatibility order is 0, 3, 1, 2 and uses a
            // triangle strip, not the generic quad winding.
            AppendStrip(triangles, new[] { 0, 3, 1, 2 });
            return new CpuMesh(vertices, triangles.ToArray());
        }

        private static CpuMesh TrailMulti(DrawSubmission submission)
        {
            if (submission.ItemCount < 4 || (submission.ItemCount & 1) != 0)
                throw new ArgumentException("A multi-trail requires an even item count of at least four.", nameof(submission));
            return Strip(submission.Points, 0, submission.ItemCount / 2, texcoord: true);
        }

        private static CpuMesh TrailStack(DrawSubmission submission)
        {
            int count = submission.ItemCount;
            Require(submission.Points, checked(count * 8));
            var vertices = new RenderVertex[count * 4];
            var triangles = new List<int>(count * 6);
            for (int i = 0; i < count; i++)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 uv = submission.Points[i * 8 + corner * 2];
                    Vector3 position = submission.Points[i * 8 + corner * 2 + 1];
                    vertices[i * 4 + corner] = Vertex(position, new Vector2(uv.X, uv.Y));
                }
                AppendQuad(triangles, i * 4, i * 4 + 1, i * 4 + 2, i * 4 + 3);
            }
            return new CpuMesh(vertices, triangles.ToArray());
        }

        private static int[] BuildRangePair(int first, int second, int count)
        {
            var result = new int[count * 2 + 2];
            for (int i = 0; i < count; i++)
            {
                result[i * 2] = first + i;
                result[i * 2 + 1] = second + i;
            }
            result[count * 2] = first;
            result[count * 2 + 1] = second;
            return result;
        }

        private static int[] Range(int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = i;
            return result;
        }

        private static void AppendStrip(List<int> target, int[] sequence)
        {
            for (int i = 0; i + 2 < sequence.Length; i++)
            {
                if ((i & 1) == 0)
                {
                    target.Add(sequence[i]); target.Add(sequence[i + 1]); target.Add(sequence[i + 2]);
                }
                else
                {
                    target.Add(sequence[i + 1]); target.Add(sequence[i]); target.Add(sequence[i + 2]);
                }
            }
        }

        private static void AppendQuadStrip(List<int> target, int[] sequence)
        {
            for (int i = 0; i + 3 < sequence.Length; i += 2)
            {
                int first = sequence[i];
                int second = sequence[i + 1];
                int third = sequence[i + 2];
                int fourth = sequence[i + 3];
                target.Add(first); target.Add(second); target.Add(fourth);
                target.Add(first); target.Add(fourth); target.Add(third);
            }
        }

        private static void AppendQuad(List<int> target, int first, int second, int third, int fourth)
        {
            target.Add(first); target.Add(second); target.Add(third);
            target.Add(first); target.Add(third); target.Add(fourth);
        }

        private static void AppendFan(List<int> target, int center, int first, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                target.Add(center); target.Add(first + i); target.Add(first + i + 1);
            }
            target.Add(center); target.Add(first + count - 1); target.Add(first);
        }

        private static void AppendFan(List<int> target, int[] sequence)
        {
            for (int i = 1; i < sequence.Length - 1; i++)
            {
                target.Add(sequence[0]); target.Add(sequence[i]); target.Add(sequence[i + 1]);
            }
        }

        private static void Require(Vector3[] points, int count)
        {
            if (points == null || points.Length < count)
                throw new ArgumentException($"Primitive requires {count} points.", nameof(points));
        }
    }
}
