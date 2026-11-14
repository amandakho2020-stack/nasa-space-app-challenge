using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Tiny procedural modelling kit that writes straight into an <see cref="ArrayMesh"/>.
///
/// - Every face is given an "outward" hint, and the winding is fixed automatically, so primitives
///   can be authored without worrying about Godot's clockwise front-face convention.
/// - Output is flat-shaded (per-face normals) for the faceted low-poly look.
/// - Smoothed normals (averaged over coincident vertices) are baked into CUSTOM0 so the
///   inverted-hull outline shader produces an unbroken line around hard edges.
/// - Each face gets a small random brightness jitter for a hand-painted feel.
/// - A transform stack (Push/Pop) lets models be assembled from rotated / scaled parts.
/// </summary>
public sealed class MeshKit
{
	private const int SlotCount = 4;
	private readonly List<Vector3>[] _pos = new List<Vector3>[SlotCount];
	private readonly List<Color>[] _col = new List<Color>[SlotCount];
	private readonly Stack<Transform3D> _stack = new();
	private readonly RandomNumberGenerator _rng;
	private Transform3D _xf = Transform3D.Identity;
	private bool _mirrored;

	/// <summary>Max +/- brightness variation per face (0.05 = 5%).</summary>
	public float ColorJitter { get; set; } = 0.05f;

	public RandomNumberGenerator Rng => _rng;

	public MeshKit(int seed = 7)
	{
		for (int i = 0; i < SlotCount; i++)
		{
			_pos[i] = new List<Vector3>();
			_col[i] = new List<Color>();
		}
		_rng = new RandomNumberGenerator { Seed = (ulong)seed };
	}

	// ======================================================================
	//  Transform stack
	// ======================================================================

	public void Push(Transform3D local)
	{
		_stack.Push(_xf);
		_xf = _xf * local;
		_mirrored = _xf.Basis.Determinant() < 0f;
	}

	public void Push(Vector3 origin, Vector3 rotationDegrees = default, Vector3? scale = null)
	{
		Basis b = Basis.FromEuler(rotationDegrees * (Mathf.Pi / 180f));
		if (scale.HasValue)
			b *= Basis.FromScale(scale.Value);
		Push(new Transform3D(b, origin));
	}

	public void Pop()
	{
		_xf = _stack.Pop();
		_mirrored = _xf.Basis.Determinant() < 0f;
	}

	/// <summary>Orthonormal basis whose +Y axis points along <paramref name="dir"/>.</summary>
	public static Basis BasisFromY(Vector3 dir)
	{
		Vector3 y = dir.Normalized();
		Vector3 helper = Mathf.Abs(y.Y) < 0.99f ? Vector3.Up : Vector3.Right;
		Vector3 x = helper.Cross(y).Normalized();
		Vector3 z = x.Cross(y).Normalized();
		return new Basis(x, y, z);
	}

	// ======================================================================
	//  Low-level faces
	// ======================================================================

	public Color Jitter(Color c)
	{
		float k = 1f + _rng.RandfRange(-ColorJitter, ColorJitter);
		return new Color(c.R * k, c.G * k, c.B * k, c.A);
	}

	/// <summary>Convex polygon (fan-triangulated), oriented to face <paramref name="outward"/>. One colour per face.</summary>
	public void Face(Surf s, Color col, Vector3 outward, params Vector3[] p)
	{
		Color c = Jitter(col);
		for (int i = 1; i < p.Length - 1; i++)
			Tri(s, p[0], p[i], p[i + 1], c, outward);
	}

	/// <summary>Single triangle (local space), flipped if needed so its normal points along <paramref name="outward"/>.</summary>
	public void Tri(Surf s, Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 outward)
	{
		Vector3 n = (b - a).Cross(c - a);
		if (n.LengthSquared() < 1e-14f)
			return;
		if (n.Dot(outward) < 0f)
			(b, c) = (c, b);

		Vector3 wa = _xf * a, wb = _xf * b, wc = _xf * c;
		if (_mirrored)
			(wb, wc) = (wc, wb);

		int i = (int)s;
		_pos[i].Add(wa);
		_pos[i].Add(wb);
		_pos[i].Add(wc);
		_col[i].Add(col);
		_col[i].Add(col);
		_col[i].Add(col);
	}

	private void Quad(Surf s, Color col, Vector3 center, Vector3 u, Vector3 v, Vector3 outward) =>
		Face(s, col, outward, center - u - v, center + u - v, center + u + v, center - u + v);

	// ======================================================================
	//  Primitives
	// ======================================================================

	public void Box(Surf s, Vector3 c, Vector3 size, Color col)
	{
		Vector3 h = size * 0.5f;
		var X = new Vector3(h.X, 0, 0);
		var Y = new Vector3(0, h.Y, 0);
		var Z = new Vector3(0, 0, h.Z);
		Quad(s, col, c + X, Y, Z, Vector3.Right);
		Quad(s, col, c - X, Y, Z, Vector3.Left);
		Quad(s, col, c + Y, X, Z, Vector3.Up);
		Quad(s, col, c - Y, X, Z, Vector3.Down);
		Quad(s, col, c + Z, X, Y, Vector3.Back);
		Quad(s, col, c - Z, X, Y, Vector3.Forward);
	}

	/// <summary>Rotated box (rotation in degrees, Godot YXZ order like Node3D.RotationDegrees).</summary>
	public void Box(Surf s, Vector3 c, Vector3 size, Color col, Vector3 rotationDegrees)
	{
		Push(c, rotationDegrees);
		Box(s, Vector3.Zero, size, col);
		Pop();
	}

	/// <summary>
	/// Cylinder / frustum / cone along local Y, centred on <paramref name="c"/>.
	/// r0 = bottom radius, r1 = top radius (0 makes a cone). <paramref name="inward"/> flips the side
	/// faces to point inside (use with a second, normal call to make an open bowl visible from both sides).
	/// </summary>
	public void Cylinder(Surf s, Vector3 c, float r0, float r1, float h, int sides, Color col,
		bool caps = true, Color? capColor = null, bool inward = false, float startAngle = 0f)
	{
		var bot = new Vector3[sides];
		var top = new Vector3[sides];
		for (int i = 0; i < sides; i++)
		{
			float a = startAngle + i * Mathf.Tau / sides;
			var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			bot[i] = c + d * r0 - new Vector3(0, h / 2f, 0);
			top[i] = c + d * r1 + new Vector3(0, h / 2f, 0);
		}
		for (int i = 0; i < sides; i++)
		{
			int j = (i + 1) % sides;
			float am = startAngle + (i + 0.5f) * Mathf.Tau / sides;
			var outward = new Vector3(Mathf.Cos(am), (r0 - r1) / Mathf.Max(h, 0.001f), Mathf.Sin(am));
			if (inward)
				outward = -outward;
			Face(s, col, outward, bot[i], bot[j], top[j], top[i]);
		}
		if (!caps)
			return;
		if (r0 > 1e-4f)
			Face(s, capColor ?? col, inward ? Vector3.Up : Vector3.Down, bot);
		if (r1 > 1e-4f)
			Face(s, capColor ?? col, inward ? Vector3.Down : Vector3.Up, top);
	}

	/// <summary>Cylinder (or frustum when r1 is given) spanning the segment a -> b.</summary>
	public void CylinderBetween(Surf s, Vector3 a, Vector3 b, float r0, int sides, Color col, float? r1 = null, bool caps = true)
	{
		Vector3 d = b - a;
		float len = d.Length();
		if (len < 1e-5f)
			return;
		Push(new Transform3D(BasisFromY(d), (a + b) / 2f));
		Cylinder(s, Vector3.Zero, r0, r1 ?? r0, len, sides, col, caps);
		Pop();
	}

	/// <summary>Square-section beam (box) spanning the segment a -> b.</summary>
	public void Beam(Surf s, Vector3 a, Vector3 b, float thickness, Color col)
	{
		Vector3 d = b - a;
		float len = d.Length();
		if (len < 1e-5f)
			return;
		Push(new Transform3D(BasisFromY(d), (a + b) / 2f));
		Box(s, Vector3.Zero, new Vector3(thickness, len, thickness), col);
		Pop();
	}

	/// <summary>Bent pipe through a list of points, with ball joints.</summary>
	public void Pipe(Surf s, Vector3[] path, float radius, int sides, Color col)
	{
		for (int i = 0; i < path.Length - 1; i++)
			CylinderBetween(s, path[i], path[i + 1], radius, sides, col);
		for (int i = 1; i < path.Length - 1; i++)
			Sphere(s, path[i], Vector3.One * radius * 1.12f, 3, sides, col);
	}

	/// <summary>
	/// UV sphere / ellipsoid. <paramref name="shape"/> can remap each unit-sphere direction to a final
	/// local position (used for noise-displaced rocks). latMin/latMax in degrees make domes/bowls.
	/// </summary>
	public void Sphere(Surf s, Vector3 c, Vector3 radii, int rings, int segs, Color col,
		float latMin = -90f, float latMax = 90f, Func<Vector3, Vector3> shape = null)
	{
		var pts = new Vector3[rings + 1, segs];
		for (int j = 0; j <= rings; j++)
		{
			float lat = Mathf.DegToRad(Mathf.Lerp(latMin, latMax, j / (float)rings));
			for (int i = 0; i < segs; i++)
			{
				float lon = i * Mathf.Tau / segs;
				var unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
				Vector3 local = shape != null ? shape(unit) : unit;
				pts[j, i] = c + local * radii;
			}
		}
		for (int j = 0; j < rings; j++)
		for (int i = 0; i < segs; i++)
		{
			int k = (i + 1) % segs;
			Vector3 a = pts[j, i], b = pts[j, k], d = pts[j + 1, k], e = pts[j + 1, i];
			Vector3 centroid = (a + b + d + e) / 4f;
			Face(s, col, centroid - c, a, b, d, e);
		}
		if (latMin > -89.9f)
		{
			var ring = new Vector3[segs];
			for (int i = 0; i < segs; i++) ring[i] = pts[0, i];
			Face(s, col, Vector3.Down, ring);
		}
		if (latMax < 89.9f)
		{
			var ring = new Vector3[segs];
			for (int i = 0; i < segs; i++) ring[i] = pts[rings, i];
			Face(s, col, Vector3.Up, ring);
		}
	}

	/// <summary>Torus around local Y (rotate with <paramref name="rotationDegrees"/>). arcDegrees &lt; 360 makes a partial ring.</summary>
	public void Torus(Surf s, Vector3 c, float ringRadius, float tubeRadius, int segs, int sides, Color col,
		Vector3 rotationDegrees = default, float arcDegrees = 360f)
	{
		Push(c, rotationDegrees);
		float arc = Mathf.DegToRad(arcDegrees);
		bool closed = arcDegrees >= 359.9f;
		int ringCount = closed ? segs : segs + 1;
		var pts = new Vector3[ringCount, sides];
		for (int i = 0; i < ringCount; i++)
		{
			float t = arc * i / segs;
			var dir = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
			for (int j = 0; j < sides; j++)
			{
				float p = j * Mathf.Tau / sides;
				pts[i, j] = dir * (ringRadius + tubeRadius * Mathf.Cos(p)) + new Vector3(0, tubeRadius * Mathf.Sin(p), 0);
			}
		}
		for (int i = 0; i < segs; i++)
		{
			int i2 = closed ? (i + 1) % segs : i + 1;
			float tm = arc * (i + 0.5f) / segs;
			Vector3 center = new Vector3(Mathf.Cos(tm), 0, Mathf.Sin(tm)) * ringRadius;
			for (int j = 0; j < sides; j++)
			{
				int j2 = (j + 1) % sides;
				Vector3 a = pts[i, j], b = pts[i2, j], d = pts[i2, j2], e = pts[i, j2];
				Face(s, col, (a + b + d + e) / 4f - center, a, b, d, e);
			}
		}
		Pop();
	}

	/// <summary>
	/// Prism: a 2D polygon in local XY (any winding, may be concave) extruded along Z from z0 to z1.
	/// </summary>
	public void Extrude(Surf s, Vector2[] polygon, float z0, float z1, Color col, Color? capColor = null)
	{
		Vector2[] p = polygon;
		float area = 0f;
		for (int i = 0; i < p.Length; i++)
		{
			Vector2 a = p[i], b = p[(i + 1) % p.Length];
			area += a.X * b.Y - b.X * a.Y;
		}
		if (area < 0f)
		{
			p = (Vector2[])polygon.Clone();
			Array.Reverse(p);
		}

		for (int i = 0; i < p.Length; i++)
		{
			Vector2 a = p[i], b = p[(i + 1) % p.Length];
			var outward = new Vector3(b.Y - a.Y, -(b.X - a.X), 0f); // CCW polygon: (dy, -dx) points out
			Face(s, col, outward, new Vector3(a.X, a.Y, z0), new Vector3(b.X, b.Y, z0), new Vector3(b.X, b.Y, z1), new Vector3(a.X, a.Y, z1));
		}

		int[] idx = Geometry2D.TriangulatePolygon(p);
		Color front = Jitter(capColor ?? col), back = Jitter(capColor ?? col);
		for (int t = 0; t + 2 < idx.Length; t += 3)
		{
			Vector2 a = p[idx[t]], b = p[idx[t + 1]], c = p[idx[t + 2]];
			Tri(s, new Vector3(a.X, a.Y, z1), new Vector3(b.X, b.Y, z1), new Vector3(c.X, c.Y, z1), front, Vector3.Back);
			Tri(s, new Vector3(a.X, a.Y, z0), new Vector3(b.X, b.Y, z0), new Vector3(c.X, c.Y, z0), back, Vector3.Forward);
		}
	}

	/// <summary>
	/// Curved plate (e.g. a fender) following an arc in the local Y-Z plane around <paramref name="c"/>:
	/// angle 0 = +Z, 90 = +Y. Width runs along X.
	/// </summary>
	public void ArcPlate(Surf s, Vector3 c, float radius, float width, float thickness, float a0Deg, float a1Deg, int segs, Color col)
	{
		Vector3 P(float deg, float r) => c + new Vector3(0, Mathf.Sin(Mathf.DegToRad(deg)), Mathf.Cos(Mathf.DegToRad(deg))) * r;
		var hx = new Vector3(width / 2f, 0, 0);
		for (int i = 0; i < segs; i++)
		{
			float d0 = Mathf.Lerp(a0Deg, a1Deg, i / (float)segs), d1 = Mathf.Lerp(a0Deg, a1Deg, (i + 1) / (float)segs);
			float dm = (d0 + d1) / 2f;
			Vector3 radial = P(dm, 1f) - c;
			Vector3 o0 = P(d0, radius + thickness), o1 = P(d1, radius + thickness);
			Vector3 i0 = P(d0, radius), i1 = P(d1, radius);
			Face(s, col, radial, o0 - hx, o0 + hx, o1 + hx, o1 - hx);
			Face(s, col, -radial, i0 - hx, i0 + hx, i1 + hx, i1 - hx);
			Face(s, col, Vector3.Right, o0 + hx, o1 + hx, i1 + hx, i0 + hx);
			Face(s, col, Vector3.Left, o0 - hx, o1 - hx, i1 - hx, i0 - hx);
		}
	}

	// ======================================================================
	//  Output
	// ======================================================================

	public ArrayMesh Commit()
	{
		// Smoothed normals across every slot, keyed by quantized position (1 mm).
		var smooth = new Dictionary<Vector3I, Vector3>();
		static Vector3I Key(Vector3 p) => new(Mathf.RoundToInt(p.X * 1000f), Mathf.RoundToInt(p.Y * 1000f), Mathf.RoundToInt(p.Z * 1000f));
		for (int s = 0; s < SlotCount; s++)
		{
			List<Vector3> p = _pos[s];
			for (int t = 0; t < p.Count; t += 3)
			{
				Vector3 n = (p[t + 1] - p[t]).Cross(p[t + 2] - p[t]); // area-weighted
				for (int k = 0; k < 3; k++)
				{
					Vector3I key = Key(p[t + k]);
					smooth[key] = smooth.TryGetValue(key, out Vector3 acc) ? acc + n : n;
				}
			}
		}

		var mesh = new ArrayMesh();
		var format = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
		for (int s = 0; s < SlotCount; s++)
		{
			List<Vector3> p = _pos[s];
			if (p.Count == 0)
				continue;
			List<Color> c = _col[s];
			int n = p.Count;
			var verts = new Vector3[n];
			var normals = new Vector3[n];
			var colors = new Color[n];
			var custom = new float[n * 4];
			int[] order = { 0, 2, 1 }; // stored CCW-outward; Godot front faces are clockwise
			for (int t = 0; t < n; t += 3)
			{
				Vector3 fn = (p[t + 1] - p[t]).Cross(p[t + 2] - p[t]).Normalized();
				for (int k = 0; k < 3; k++)
				{
					int src = t + order[k], dst = t + k;
					verts[dst] = p[src];
					normals[dst] = fn;
					colors[dst] = c[src];
					Vector3 sn = smooth.TryGetValue(Key(p[src]), out Vector3 acc) ? acc.Normalized() : fn;
					custom[dst * 4] = sn.X;
					custom[dst * 4 + 1] = sn.Y;
					custom[dst * 4 + 2] = sn.Z;
					custom[dst * 4 + 3] = 0f;
				}
			}

			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = verts;
			arrays[(int)Mesh.ArrayType.Normal] = normals;
			arrays[(int)Mesh.ArrayType.Color] = colors;
			arrays[(int)Mesh.ArrayType.Custom0] = custom;
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
			mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, StyleKit.Get((Surf)s));
			mesh.SurfaceSetName(mesh.GetSurfaceCount() - 1, ((Surf)s).ToString());
		}
		return mesh;
	}
}
