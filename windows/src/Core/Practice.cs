using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GeoViewer.Core
{
    /// <summary>How many roll (Z) slices the practice grid keeps track of.</summary>
    public enum PracticeMode
    {
        /// <summary>A: every axis 0..90 in 15° steps → 343 combinations per model.</summary>
        Dense = 0,

        /// <summary>B: roll limited to 0 / 45 / 90 → 147 combinations per model.</summary>
        Medium = 1,

        /// <summary>C: roll fixed at 0 → 49 combinations per model.</summary>
        Simple = 2
    }

    /// <summary>
    /// Per model record of the camera angles that have been drawn. Every model keeps
    /// its own set, the record survives restarts and is stored in %APPDATA%.
    /// </summary>
    public sealed class PracticeBook
    {
        public const int Step = 15;

        /// <summary>All preset values of one axis, in degrees.</summary>
        public static readonly int[] Angles = { 0, 15, 30, 45, 60, 75, 90 };

        private static readonly int[] MediumRoll = { 0, 45, 90 };
        private static readonly int[] SimpleRoll = { 0 };

        private readonly Dictionary<int, HashSet<int>> _done = new Dictionary<int, HashSet<int>>();

        public PracticeMode Mode { get; private set; } = PracticeMode.Simple;

        public PracticeBook()
        {
            Load();
        }

        public static string FilePath
        {
            get
            {
                // GEO_PRACTICE_DIR lets the record live next to the exe (portable use / testing)
                string custom = Environment.GetEnvironmentVariable("GEO_PRACTICE_DIR");
                string dir = string.IsNullOrWhiteSpace(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   "GeoRef", "GeometryViewer")
                    : custom;
                return Path.Combine(dir, "practice.json");
            }
        }

        /// <summary>Roll values that are part of the current mode.</summary>
        public int[] RollLevels
        {
            get
            {
                switch (Mode)
                {
                    case PracticeMode.Dense: return Angles;
                    case PracticeMode.Medium: return MediumRoll;
                    default: return SimpleRoll;
                }
            }
        }

        /// <summary>Number of combinations tracked for one model in the current mode.</summary>
        public int Total => Angles.Length * Angles.Length * RollLevels.Length;

        public static string ModeName(PracticeMode mode)
        {
            switch (mode)
            {
                case PracticeMode.Dense: return "A · 343";
                case PracticeMode.Medium: return "B · 147";
                default: return "C · 49";
            }
        }

        public void SetMode(PracticeMode mode)
        {
            Mode = mode;
            Save();
        }

        public bool AllowsRoll(int roll) => Array.IndexOf(RollLevels, roll) >= 0;

        private static int Pack(int x, int y, int z) =>
            ((x / Step) * Angles.Length + (y / Step)) * Angles.Length + (z / Step);

        private HashSet<int> SetOf(int solid)
        {
            if (!_done.TryGetValue(solid, out HashSet<int> set))
            {
                set = new HashSet<int>();
                _done[solid] = set;
            }
            return set;
        }

        public bool IsDone(int solid, int x, int y, int z) => SetOf(solid).Contains(Pack(x, y, z));

        /// <summary>Flips the tick of one angle and returns the new state.</summary>
        public bool Toggle(int solid, int x, int y, int z)
        {
            HashSet<int> set = SetOf(solid);
            int key = Pack(x, y, z);
            bool now;
            if (set.Contains(key))
            {
                set.Remove(key);
                now = false;
            }
            else
            {
                set.Add(key);
                now = true;
            }
            Save();
            return now;
        }

        /// <summary>Number of ticked angles that belong to the current mode.</summary>
        public int DoneCount(int solid)
        {
            int count = 0;
            foreach (int key in SetOf(solid))
            {
                int zIndex = key % Angles.Length;
                if (Mode == PracticeMode.Dense
                    || (Mode == PracticeMode.Medium && zIndex % 3 == 0)
                    || (Mode == PracticeMode.Simple && zIndex == 0))
                {
                    count++;
                }
            }
            return count;
        }

        public int Remaining(int solid) => Math.Max(0, Total - DoneCount(solid));

        public void ResetSolid(int solid)
        {
            SetOf(solid).Clear();
            Save();
        }

        /// <summary>Picks a random combination that has not been ticked yet.</summary>
        public bool TryPick(int solid, Random rng, out int x, out int y, out int z)
        {
            x = y = z = 0;
            if (Remaining(solid) <= 0) return false;
            for (int guard = 0; guard < 500; guard++)
            {
                int xi = rng.Next(Angles.Length);
                int yi = rng.Next(Angles.Length);
                int zi = rng.Next(RollLevels.Length);
                int px = Angles[xi], py = Angles[yi], pz = RollLevels[zi];
                if (IsDone(solid, px, py, pz)) continue;
                x = px;
                y = py;
                z = pz;
                return true;
            }
            // fall back to a linear scan so the pick always succeeds
            foreach (int px in Angles)
            {
                foreach (int py in Angles)
                {
                    foreach (int pz in RollLevels)
                    {
                        if (IsDone(solid, px, py, pz)) continue;
                        x = px;
                        y = py;
                        z = pz;
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ storage

        private sealed class Payload
        {
            public int Version { get; set; } = 1;
            public int Mode { get; set; }
            public Dictionary<string, List<int>> Solids { get; set; } = new Dictionary<string, List<int>>();
        }

        public void Load()
        {
            _done.Clear();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;
                Payload data = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path));
                if (data == null) return;
                Mode = (PracticeMode)Math.Clamp(data.Mode, 0, 2);
                if (data.Solids == null) return;
                foreach (KeyValuePair<string, List<int>> pair in data.Solids)
                {
                    if (!int.TryParse(pair.Key, out int solid) || pair.Value == null) continue;
                    HashSet<int> set = SetOf(solid);
                    foreach (int key in pair.Value) set.Add(key);
                }
            }
            catch (Exception)
            {
                // a broken file must never stop the viewer from starting
                _done.Clear();
            }
        }

        public void Save()
        {
            try
            {
                var data = new Payload { Version = 1, Mode = (int)Mode };
                foreach (KeyValuePair<int, HashSet<int>> pair in _done)
                {
                    if (pair.Value.Count == 0) continue;
                    data.Solids[pair.Key.ToString()] = new List<int>(pair.Value);
                }
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(data));
            }
            catch (Exception)
            {
                // ignore: practice progress is a convenience, not data the user can lose money on
            }
        }
    }
}
