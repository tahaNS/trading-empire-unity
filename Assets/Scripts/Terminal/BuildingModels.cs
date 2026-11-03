using System;
using TradingEmpire.Core;
using UnityEngine;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The building models, ported number for number from the web version's procedural models: shops with striped awnings, apartment blocks with
    /// balconies, glass offices and towers, houses with pitched roofs and picket fences, a hotel with a pool, parks, a fountain plaza, and the
    /// Trading HQ with its ticker. Each is built centred on the origin with its base at y = 0, unrotated; the caller turns it.
    /// </summary>
    public static class BuildingModels
    {
        const int Light = 0xe9edf2;
        static readonly int[] Roofs = { 0xb4503c, 0x8a5a44, 0x4a6fa5, 0x6b8e4e, 0x9c4a6b };
        static readonly string[] Varied = { "house", "small-shop", "apartment", "restaurant", "hotel" };

        static Color H(int rgb) { return MeshBuilder.Hex(rgb); }
        static Color Sh(int rgb, float f) { return MeshBuilder.Shade(H(rgb), f); }

        /// <summary>A small deterministic number for a building id, so each building varies a little in colour.</summary>
        public static int SeedOf(string id)
        {
            int h = 17;
            foreach (char c in id) h = h * 31 + c;
            return Math.Abs(h % 997) + 1;
        }

        /// <summary>Houses, shops, apartments and hotels vary a little in colour from one building to the next.</summary>
        static int Vary(int color, int seed)
        {
            if (seed == 0) return color;
            Color.RGBToHSV(H(color), out float h, out float s, out float v);
            // the web version varies hue and lightness (HSL); this does the same through HSV with matching steps
            h = (h + ((seed % 11) - 5) * 0.012f + 1f) % 1f;
            v = Mathf.Clamp(v + (((seed >> 3) % 5) - 2) * 0.025f, 0.28f, 0.95f);
            var c = Color.HSVToRGB(h, s, v);
            return ((int)(c.r * 255) << 16) | ((int)(c.g * 255) << 8) | (int)(c.b * 255);
        }

        static Color Lerp(int a, int b, float t) { return Color.Lerp(H(a), H(b), t); }

        static void Planters(MeshBuilder b, float W, float D)
        {
            foreach (int sx in new[] { -1, 1 }) b.Box(0.13f, 0.12f, 0.13f, sx * W * 0.4f, 0, D / 2 + 0.1f, H(0x4f9d4f));
        }

        static void RoofUnits(MeshBuilder b, float W, float D, float y, int n)
        {
            for (int i = 0; i < n; i++) b.Box(0.22f, 0.16f, 0.22f, (i % 2 != 0 ? 1 : -1) * W * 0.22f, y, ((i % 3) - 1) * D * 0.22f, H(0x9aa4ae));
        }

        static void Shop(MeshBuilder b, float w, float d, int color, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            if (level >= 3)
            {
                // mini mall: two glass-fronted floors, a deep canopy and a big rooftop sign
                float Hh = 2.3f;
                b.Block(2, H(0x5f93c4), H(0x3d5f85), W, Hh, D, 0, 0, 0, 2);
                b.Box(W + 0.1f, 0.1f, D + 0.1f, 0, Hh, 0, H(0xeaeef2));
                b.Box(W + 0.1f, 0.1f, 0.45f, 0, 0.75f, D / 2 + 0.2f, H(0xd9534f));
                b.Box(0.7f, 0.34f, 0.08f, 0, Hh + 0.2f, 0, H(0xffd36b), 0, null, 0, 0, 0, 1, 0.5f);
                RoofUnits(b, W, D, Hh + 0.1f, 2);
                return;
            }
            float h = level == 2 ? 1.9f : 1.15f;
            b.Block(3, H(color), Sh(color, 0.7f), W, h, D, 0, 0, 0, level == 2 ? 2 : 1);
            b.Box(W + 0.06f, 0.1f, D + 0.06f, 0, h, 0, Sh(color, 0.55f));
            // striped awning over the front (+z)
            const int stripes = 6;
            for (int i = 0; i < stripes; i++)
                b.Box(W / stripes, 0.05f, 0.4f, -W / 2 + (i + 0.5f) * (W / stripes), 0.78f, D / 2 + 0.16f, i % 2 != 0 ? H(0xffffff) : H(0xd9534f), 0.45f);
            b.Box(0.42f, 0.18f, 0.05f, 0, h + 0.13f, D / 2 - 0.05f, H(0xffd36b), 0, null, 0, 0, 0, 1, 0.5f);
            RoofUnits(b, W, D, h + 0.1f, level == 2 ? 2 : 1);
            Planters(b, W, D);
        }

        static void Apartment(MeshBuilder b, float w, float d, int color, float height, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            float h = height * new[] { 1f, 1.35f, 1.7f }[level - 1];
            int floors = new[] { 4, 5, 7 }[level - 1];
            var wall = Color.Lerp(H(color), H(0xf4efe6), 0.55f);
            b.Block(1, wall, Sh(color, 0.6f), W, h, D, 0, 0, 0, floors);
            b.Box(W + 0.1f, 0.1f, D + 0.1f, 0, h, 0, Sh(color, 0.5f));
            for (int i = 1; i < floors; i++) b.Box(W * 0.6f, 0.05f, 0.22f, 0, h / floors * i, D / 2 + 0.1f, H(Light));
            if (level >= 2) for (int i = 1; i < floors; i++) b.Box(0.22f, 0.05f, D * 0.6f, W / 2 + 0.1f, h / floors * i, 0, H(Light)); // second wing of balconies
            if (level >= 3) b.Block(1, wall, Sh(color, 0.6f), W * 0.6f, 0.9f, D * 0.6f, 0, h + 0.1f, 0, 1); // penthouse
            b.Box(0.5f, 0.35f, 0.5f, W / 4, h + 0.1f + (level >= 3 ? 0.9f : 0f), -D / 4, H(0x8a94a0));
            b.Cylinder(0.2f, 0.4f, -W / 4, h + 0.1f, D / 5, H(0xb98b5e));
            b.Box(W * 0.35f, 0.06f, 0.3f, 0, 0.55f, D / 2 + 0.15f, H(0xd9534f)); // entrance canopy
            Planters(b, W, D);
        }

        static void Office(MeshBuilder b, float w, float d, float height, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            float h = height * new[] { 1f, 1.2f, 1.5f }[level - 1];
            int floors = new[] { 7, 9, 12 }[level - 1];
            b.Block(2, H(0x44607f), H(0x39506b), W, h * 0.82f, D, 0, 0, 0, floors);
            b.Box(W * 0.62f, 0.1f, D * 0.62f, 0, h * 0.82f, 0, H(0x2f4258));
            b.Block(2, H(0x44607f), H(0x39506b), W * 0.62f, h * 0.18f, D * 0.62f, 0, h * 0.82f + 0.1f, 0, 1);
            if (level >= 2) b.Block(2, H(0x4f6f93), H(0x39506b), W * 0.45f, 1.2f, D * 0.3f, 0, 0, D / 2 + 0.1f, 2); // lobby annex
            if (level >= 3)
            {
                b.Cylinder(0.04f, 1.0f, 0, h + 0.1f, 0, H(0xcfd6dc));
                b.Cone(0.09f, 0.2f, 0, h + 1.1f, 0, H(0xe5484d), 0.6f);
            }
            else
            {
                b.Box(0.4f, 0.22f, 0.4f, -W / 5, h + 0.12f, 0, H(0xa4adb7));
                b.Box(0.3f, 0.18f, 0.3f, W / 5, h + 0.12f, D / 6, H(0xa4adb7));
            }
            b.Box(W * 0.5f, 0.35f, 0.12f, 0, 0, D / 2 + 0.05f, H(0x2b3a4a)); // entrance canopy base
        }

        static void Restaurant(MeshBuilder b, float w, float d, int color, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            float h = level == 1 ? 1.55f : 1.85f;
            b.Block(3, H(color), Sh(color, 0.6f), W, h, D * 0.65f, 0, 0, -D * 0.17f, level);
            b.Box(W + 0.06f, 0.1f, D * 0.65f + 0.06f, 0, h, -D * 0.17f, Sh(color, 0.5f));
            const int stripes = 8;
            for (int i = 0; i < stripes; i++)
                b.Box(W / stripes, 0.05f, 0.5f, -W / 2 + (i + 0.5f) * (W / stripes), 1.0f, D * 0.15f + 0.15f, i % 2 != 0 ? H(0xf6e7c9) : H(0x2f7d58), 0.4f);
            // terrace tables with umbrellas (more of them at higher levels)
            var tables = new[] { (-0.5f, 0.62f, 0xd9534f), (0.5f, 0.62f, 0x2f7d58), (0f, 0.8f, 0xe0a458) };
            for (int i = 0; i < Math.Min(level + 1, 3); i++)
            {
                var (tx, tz, uc) = tables[i];
                b.Cylinder(0.14f, 0.12f, tx, 0.28f, tz, H(0xf4f1ea));
                b.Cylinder(0.03f, 0.3f, tx, 0, tz, H(0x6d6d6d));
                b.Cone(0.3f, 0.16f, tx, 0.62f, tz, H(uc));
            }
            if (level >= 3) b.Block(3, H(color), Sh(color, 0.6f), W * 0.5f, 0.8f, D * 0.3f, 0, h + 0.1f, -D * 0.17f, 1); // roof dining room
            b.Box(0.9f, 0.3f, 0.06f, 0, h + 0.15f, -D * 0.17f + D * 0.325f - 0.06f, H(0xfff0b0), 0, null, 0, 0, 0, 1, 0.5f);
        }

        static void Tower(MeshBuilder b, float w, float d, float height, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            float h = height * new[] { 1f, 1.15f, 1.3f }[level - 1];
            b.Block(2, H(0x3d8f8a), H(0x2d6f6b), W, h * 0.3f, D, 0, 0, 0, 3);
            b.Box(W + 0.12f, 0.1f, D + 0.12f, 0, h * 0.3f, 0, H(0xdfe8e8));
            b.Block(2, H(0x3d8f8a), H(0x2d6f6b), W * 0.78f, h * 0.46f, D * 0.78f, 0, h * 0.3f + 0.1f, 0, 6);
            b.Box(W * 0.78f + 0.1f, 0.08f, D * 0.78f + 0.1f, 0, h * 0.76f + 0.1f, 0, H(0xdfe8e8));
            b.Block(2, H(0x3d8f8a), H(0x2d6f6b), W * 0.58f, h * 0.24f - 0.18f, D * 0.58f, 0, h * 0.76f + 0.18f, 0, 3);
            if (level >= 2) b.Box(W * 0.62f, 0.12f, D * 0.62f, 0, h - 0.1f, 0, H(0xe0b64a)); // gold crown band
            if (level >= 3) b.Box(W * 0.3f, 0.7f, D * 0.3f, 0, h, 0, H(0x2d6f6b));
            b.Cylinder(0.05f, 0.9f, 0, h + (level >= 3 ? 0.7f : 0f), 0, H(0xcfd6dc));
            b.Cone(0.1f, 0.25f, 0, h + 0.9f + (level >= 3 ? 0.7f : 0f), 0, H(0xe5484d), 0.6f);
        }

        static void Corporate(MeshBuilder b, float w, float d, float height, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            float h = height * new[] { 1f, 1.2f, 1.5f }[level - 1];
            b.Block(2, H(0x4a5fa8), H(0x34457d), W, 1.4f, D * 0.45f, 0, 0, D * 0.27f, 2); // low lobby wing
            b.Box(W + 0.1f, 0.08f, D * 0.45f + 0.1f, 0, 1.4f, D * 0.27f, H(0xe6e9f2));
            b.Block(2, H(0x4a5fa8), H(0x34457d), W, h, D * 0.5f, 0, 0, -D * 0.22f, 9 + (level - 1) * 3);
            b.Box(W + 0.1f, 0.1f, D * 0.5f + 0.1f, 0, h, -D * 0.22f, H(0xe6e9f2));
            b.Block(2, H(0x5a70c0), H(0x34457d), W * 0.28f, h * 0.35f, D * 0.28f, W * 0.3f, h + 0.1f, -D * 0.22f, 3);
            if (level >= 2) b.Block(2, H(0x5a70c0), H(0x34457d), W * 0.28f, h * 0.2f, D * 0.28f, -W * 0.3f, h + 0.1f, -D * 0.22f, 2);
            if (level >= 3)
            {
                b.Cylinder(0.04f, 1.1f, W * 0.3f, h + 0.1f + h * 0.35f, -D * 0.22f, H(0xcfd6dc));
                b.Cone(0.09f, 0.2f, W * 0.3f, h + 0.1f + h * 0.35f + 1.1f, -D * 0.22f, H(0xe5484d), 0.6f);
            }
            else b.Box(0.5f, 0.25f, 0.5f, -W * 0.2f, h + 0.1f, -D * 0.22f, H(0xa4adb7));
        }

        static void House(MeshBuilder b, float w, float d, int color, int level, int seed)
        {
            int roofColor = Roofs[seed % Roofs.Length];
            float W = w * 0.78f, D = d * 0.78f;
            float h = level == 1 ? 0.8f : 1.15f;
            b.Box(W, h, D, 0, 0, 0, H(color));
            // pitched roof: two rotated boards, visibly different from the flat-roofed blocks
            b.Box(W * 1.1f, 0.12f, D * 0.62f, 0, h + 0.2f, -D * 0.2f, H(roofColor), -0.6f);
            b.Box(W * 1.1f, 0.12f, D * 0.62f, 0, h + 0.2f, D * 0.2f, Sh(roofColor, 0.88f), 0.6f);
            // low picket fence across the front
            b.Box(W * 1.2f, 0.03f, 0.02f, 0, 0.07f, D / 2 + 0.1f, H(0xf4f1ea));
            foreach (float fx in new[] { -0.5f, -0.17f, 0.17f, 0.5f }) b.Box(0.03f, 0.12f, 0.03f, fx * W * 1.1f, 0, D / 2 + 0.1f, H(0xf4f1ea));
            b.Box(0.2f, 0.32f, 0.04f, 0, 0, D / 2 + 0.02f, H(0x5b3a29)); // door
            b.Box(0.16f, 0.16f, 0.04f, -W * 0.28f, h * 0.5f, D / 2 + 0.02f, H(0x9fcbe8));
            b.Box(0.16f, 0.16f, 0.04f, W * 0.28f, h * 0.5f, D / 2 + 0.02f, H(0x9fcbe8));
            if (level >= 2) b.Box(W * 0.45f, 0.5f, D * 0.5f, W * 0.62f, 0, 0, Sh(color, 0.9f)); // garage wing
            b.Box(0.12f, 0.3f, 0.12f, W * 0.28f, h + 0.3f, -D * 0.15f, H(0x8a5a44)); // chimney
        }

        static void Hotel(MeshBuilder b, float w, float d, int color, float height, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            float h = height * new[] { 1f, 1.2f, 1.4f }[level - 1];
            var wall = Color.Lerp(H(color), H(0xf4efe6), 0.45f);
            b.Block(1, wall, Sh(color, 0.6f), W, 1.2f, D, 0, 0, 0, 1); // podium
            b.Box(W + 0.12f, 0.1f, D + 0.12f, 0, 1.2f, 0, H(0xf4efe6));
            b.Block(1, wall, Sh(color, 0.6f), W * 0.78f, h - 1.3f, D * 0.78f, 0, 1.3f, 0, Mathf.RoundToInt(h * 1.6f));
            b.Box(W * 0.78f + 0.1f, 0.1f, D * 0.78f + 0.1f, 0, h, 0, Sh(color, 0.5f));
            b.Box(W * 0.7f, 0.06f, 0.5f, 0, 0.9f, D / 2 + 0.2f, H(0xe5484d)); // entrance canopy
            b.Box(0.8f, 0.22f, 0.06f, 0, h + 0.12f, D * 0.39f - 0.03f, H(0xffd36b), 0, null, 0, 0, 0, 1, 0.5f); // HOTEL sign
            if (level >= 2) b.Box(W * 0.5f, 0.05f, D * 0.3f, 0, 1.25f, D * 0.68f, H(0x5aa9e6)); // pool
            if (level >= 3) b.Cylinder(0.3f, 0.1f, -W * 0.28f, h + 0.1f, 0, H(0xdfe8e8)); // roof helipad
        }

        static void TradingHq(MeshBuilder b, float w, float d, int color, int level)
        {
            float W = w * 0.9f, D = d * 0.9f;
            var teal = H(color);
            var ticker = H(0xffc857);
            b.Box(W + 0.2f, 0.12f, D + 0.2f, 0, 0, 0, H(0xe9edf2)); // base plinth and lobby
            b.Block(2, teal, H(0x1f5560), W, 1.6f, D, 0, 0.12f, 0, 2);
            b.Box(W + 0.1f, 0.08f, 0.1f, 0, 1.32f, D / 2 + 0.05f, ticker, 0, null, 0, 0, 0, 1, 0.7f); // ticker band over the entrance
            if (level >= 2)
            {
                b.Block(2, teal, H(0x1f5560), W * 0.8f, 1.9f, D * 0.8f, 0, 1.8f, 0, 3);
                b.Box(W * 0.8f + 0.1f, 0.08f, 0.1f, 0, 2.9f, D * 0.4f + 0.05f, ticker, 0, null, 0, 0, 0, 1, 0.7f);
            }
            if (level >= 3)
            {
                b.Block(2, teal, H(0x1f5560), W * 0.55f, 2.6f, D * 0.55f, 0, 3.8f, 0, 5);
                b.Box(W * 0.55f + 0.1f, 0.1f, D * 0.55f + 0.1f, 0, 6.4f, 0, H(0xe9edf2));
                b.Cylinder(0.04f, 1.0f, 0, 6.5f, 0, H(0xcfd6dc));
                b.Cone(0.1f, 0.22f, 0, 7.5f, 0, H(0xffa21a), 0.8f);
                b.Box(W * 0.55f + 0.1f, 0.1f, 0.1f, 0, 5.2f, D * 0.275f + 0.05f, ticker, 0, null, 0, 0, 0, 1, 0.7f);
            }
        }

        public static void Tree(MeshBuilder b, float x, float z, float s)
        {
            b.Cylinder(0.05f * s, 0.35f * s, x, 0, z, H(0x7a5a3a), 0, 6);
            b.Box(0.5f * s, 0.5f * s, 0.5f * s, x, 0.3f * s, z, H(0x3f8f4a));
        }

        static void Park(MeshBuilder b, float w, float d, int level)
        {
            float W = w * 0.92f, D = d * 0.92f;
            b.Box(W, 0.06f, D, 0, 0, 0, H(0x6cc36a));
            b.Box(W * 0.12f, 0.07f, D, 0, 0, 0, H(0xe3d9bd)); // path
            Tree(b, -W * 0.3f, -D * 0.3f, 1.1f);
            Tree(b, W * 0.32f, D * 0.28f, 1.0f);
            b.Box(0.36f, 0.1f, 0.12f, -W * 0.28f, 0.07f, D * 0.25f, H(0x8a5a44)); // bench
            if (level >= 2)
            {
                Tree(b, W * 0.3f, -D * 0.3f, 1.2f);
                Tree(b, -W * 0.3f, D * 0.3f, 0.9f);
                b.Cylinder(0.22f, 0.05f, 0, 0.06f, 0, H(0x5aa9e6)); // pond
                b.Box(0.14f, 0.1f, 0.14f, -0.4f, 0.06f, 0, H(0xe86a92));
                b.Box(0.14f, 0.1f, 0.14f, 0.4f, 0.06f, 0.1f, H(0xe86a92));
            }
        }

        static void Plaza(MeshBuilder b, float w, float d)
        {
            float W = w * 0.95f, D = d * 0.95f;
            b.Box(W, 0.08f, D, 0, 0, 0, H(0xd8d2c4));
            b.Box(W * 0.8f, 0.1f, D * 0.8f, 0, 0, 0, H(0xe9e4d6));
            b.Cylinder(0.7f, 0.22f, 0, 0.08f, 0, H(0xbfc7ce)); // fountain basin
            b.Cylinder(0.55f, 0.05f, 0, 0.3f, 0, H(0x5aa9e6));
            b.Cylinder(0.1f, 0.5f, 0, 0.3f, 0, H(0xbfc7ce)); // centre column
            b.Cone(0.22f, 0.12f, 0, 0.8f, 0, H(0x9fd6ff));
            foreach (var (x, z) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                Tree(b, x * W * 0.4f, z * D * 0.4f, 1.0f);
                b.Box(0.34f, 0.1f, 0.12f, x * W * 0.2f, 0.1f, z * D * 0.42f, H(0x8a5a44)); // bench
            }
        }

        static void Generic(MeshBuilder b, float w, float d, int color, float height)
        {
            b.Box(w * 0.9f, height, d * 0.9f, 0, 0, 0, H(color));
            b.Box(w * 0.95f, 0.12f, d * 0.95f, 0, height, 0, Sh(color, 0.7f));
        }

        /// <summary>Build the model for a definition at an upgrade level (1-based, unrotated footprint).</summary>
        public static MeshBuilder Build(BuildingDefinition def, int level, int seed)
        {
            var b = new MeshBuilder();
            float w = def.Footprint.W, d = def.Footprint.H, height = def.Height;
            int color = Array.IndexOf(Varied, def.Id) >= 0 ? Vary(def.Color, seed) : def.Color;
            int lv = Math.Min(Math.Max(level, 1), 3);
            switch (def.Id)
            {
                case "small-shop": Shop(b, w, d, color, lv); break;
                case "apartment": Apartment(b, w, d, color, height, lv); break;
                case "small-office": Office(b, w, d, height, lv); break;
                case "restaurant": Restaurant(b, w, d, color, lv); break;
                case "luxury-tower": Tower(b, w, d, height, lv); break;
                case "corporate-office": Corporate(b, w, d, height, lv); break;
                case "house": House(b, w, d, color, lv, seed); break;
                case "hotel": Hotel(b, w, d, color, height, lv); break;
                case "trading-hq": TradingHq(b, w, d, color, lv); break;
                case "park": Park(b, w, d, lv); break;
                case "plaza": Plaza(b, w, d); break;
                default: Generic(b, w, d, color, height); break;
            }
            return b;
        }
    }
}
