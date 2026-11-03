using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TradingEmpire.Core
{
    /// <summary>Where saves live: one text value per key. The game uses files; tests use memory.</summary>
    public interface ISaveStorage
    {
        string Read(string key);
        void Write(string key, string text);
        void Delete(string key);
    }

    public sealed class MemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Items = new Dictionary<string, string>();
        public string Read(string key) { return Items.TryGetValue(key, out var v) ? v : null; }
        public void Write(string key, string text) { Items[key] = text; }
        public void Delete(string key) { Items.Remove(key); }
    }

    /// <summary>One file per key in a folder. Writes go to a temporary file first and are then moved into place, so a crash never leaves half a save.</summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        readonly string _dir;
        public FileSaveStorage(string dir) { _dir = dir; Directory.CreateDirectory(dir); }

        string PathOf(string key)
        {
            var safe = new StringBuilder();
            foreach (char c in key) safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return Path.Combine(_dir, safe + ".json");
        }

        public string Read(string key) { var p = PathOf(key); return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null; }

        public void Write(string key, string text)
        {
            var p = PathOf(key);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
        }

        public void Delete(string key) { var p = PathOf(key); if (File.Exists(p)) File.Delete(p); }
    }

    public sealed class LoadOutcome
    {
        public bool Ok;
        public string Error;
        /// <summary>Something the player should know, e.g. that the newest save was damaged and a backup was used.</summary>
        public string Notice;
    }

    /// <summary>
    /// Saves and loads the game. Every save is validated before anything is written, the previous good save is kept as a backup (a
    /// damaged one is parked, never lost), and one dated copy per day is kept for the newest few days.
    /// </summary>
    public sealed class SaveManager
    {
        public const int DailyBackups = 5;
        readonly GameWorld _world;
        readonly ISaveStorage _storage;
        readonly Func<DateTimeOffset> _now;
        string _lastBodyHash;

        public SaveManager(GameWorld world, ISaveStorage storage, Func<DateTimeOffset> now = null)
        {
            _world = world; _storage = storage; _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>FNV-1a 32-bit hash as hex. Detects corruption/truncation; not a security measure.</summary>
        public static string Fnv1a(string text)
        {
            uint h = 0x811c9dc5;
            unchecked { foreach (char c in text) { h ^= c; h *= 0x01000193; } }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }

        static string DatasetKey(string id) { return "dataset-" + id.Replace(':', '_'); }

        static string Record(string json, DateTimeOffset at)
        {
            return Json.Serialize(JsonValue.Obj().Set("json", json).Set("checksum", Fnv1a(json)).Set("savedAt", at.ToUnixTimeMilliseconds()).Set("version", SaveCodec.CurrentVersion));
        }

        /// <summary>Text -> parsed -> candles found -> consistent. Never throws.</summary>
        ParsedSave Prepare(string json, out List<Candle> candles, IReadOnlyList<Candle> known = null)
        {
            candles = null;
            var parsed = SaveCodec.Parse(json);
            if (!parsed.Ok) return parsed;
            var d = parsed.Data;
            candles = SaveCodec.CandlesFor(d);
            // a save being written is checked against the live dataset, which the save file itself does not contain
            if (candles == null && known != null && d.Dataset.Id == "upload:" + TradingSession.HashCandles(known)) candles = known.ToList();
            if (candles == null && !d.Dataset.Synthetic)
            {
                var stored = _storage.Read(DatasetKey(d.Dataset.Id));
                if (stored != null)
                {
                    try { candles = SaveCodec.DecodeCandles(Json.Parse(stored).Get("candles")); } catch (JsonException) { candles = null; }
                    if (candles != null && d.Dataset.Id != "upload:" + TradingSession.HashCandles(candles)) candles = null;
                }
            }
            if (candles == null) return new ParsedSave { Ok = false, Error = "The data file for this save is missing. Import the exported save file instead." };
            var problem = SaveCodec.CheckConsistency(d, candles.Count);
            if (problem != null) return new ParsedSave { Ok = false, Error = problem };
            return parsed;
        }

        // ---------------------------------------------------------------- saving

        /// <summary>The save as text for the Export button: self-contained (uploaded data is embedded), and in the web version's format.</summary>
        public string ExportText() { return Json.Serialize(SaveCodec.Capture(_world, _now(), true), true); }

        /// <summary>Save now. With <paramref name="onlyIfChanged"/>, nothing is written when the game is unchanged since the last save (autosave).</summary>
        public bool Save(bool onlyIfChanged = false, string[] error = null)
        {
            if (_world.Trading.State.Dataset == null) return false; // nothing meaningful to save yet
            var at = _now();
            var doc = SaveCodec.Capture(_world, at, false);
            string json = Json.Serialize(doc);
            var body = Json.Serialize(SaveCodec.Capture(_world, DateTimeOffset.FromUnixTimeMilliseconds(0), false));
            string bodyHash = Fnv1a(body);
            if (onlyIfChanged && bodyHash == _lastBodyHash) return true;

            // 1. The new save must pass the same checks as a load, or nothing is written.
            var check = Prepare(json, out var _, _world.Trading.DatasetCandles());
            if (!check.Ok)
            {
                if (error != null && error.Length > 0) error[0] = "Refusing to save an invalid game state: " + check.Error;
                return false;
            }
            string record = Record(json, at);

            // 2. Uploaded datasets are stored once, separately from the (small) save record.
            var ds = _world.Trading.State.Dataset;
            if (!ds.Synthetic && _storage.Read(DatasetKey(ds.Id)) == null)
                _storage.Write(DatasetKey(ds.Id), Json.Serialize(JsonValue.Obj().Set("id", ds.Id).Set("name", ds.Name).Set("candles", SaveCodec.EncodeCandles(_world.Trading.DatasetCandles()))));

            // 3. Keep the previous valid save as a backup; park a corrupt one instead of losing it.
            var current = _storage.Read("current");
            if (current != null) _storage.Write(IsValidRecord(current) ? "backup" : "corrupt", current);
            _storage.Write("current", record);

            // Once a day, also keep a dated copy (the newest few days), so a bad day can be undone.
            string day = at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var index = BackupIndex();
            if (!index.Contains(day))
            {
                var next = index.Concat(new[] { day }).OrderBy(x => x, StringComparer.Ordinal).ToList();
                int drop = Math.Max(next.Count - DailyBackups, 0);
                foreach (var old in next.Take(drop)) _storage.Delete("daily-" + old);
                next = next.Skip(drop).ToList();
                _storage.Write("daily-" + day, record);
                _storage.Write("daily-index", string.Join(",", next));
            }

            // 4. Read it back: the stored record must be intact.
            var back = _storage.Read("current");
            if (back != record)
            {
                if (error != null && error.Length > 0) error[0] = "The save could not be verified after writing.";
                return false;
            }
            _lastBodyHash = bodyHash;
            return true;
        }

        public List<string> BackupIndex()
        {
            var raw = _storage.Read("daily-index");
            return string.IsNullOrEmpty(raw) ? new List<string>() : raw.Split(',').Where(x => x.Length > 0).ToList();
        }

        /// <summary>Days (newest first) that have a dated backup.</summary>
        public List<string> ListBackups() { return BackupIndex().OrderByDescending(x => x, StringComparer.Ordinal).ToList(); }

        // ---------------------------------------------------------------- loading

        bool IsValidRecord(string record)
        {
            try
            {
                var r = Json.Parse(record);
                var json = r.Get("json");
                var sum = r.Get("checksum");
                if (json == null || sum == null || json.Kind != JsonKind.String || sum.Kind != JsonKind.String) return false;
                return Fnv1a(json.String) == sum.String && Prepare(json.String, out var _).Ok;
            }
            catch (JsonException) { return false; }
        }

        LoadOutcome LoadRecord(string record)
        {
            string json;
            try
            {
                var r = Json.Parse(record);
                var j = r.Get("json");
                var sum = r.Get("checksum");
                if (j == null || sum == null || j.Kind != JsonKind.String || sum.Kind != JsonKind.String) return new LoadOutcome { Error = "The stored save is damaged." };
                if (Fnv1a(j.String) != sum.String) return new LoadOutcome { Error = "The stored save failed its integrity check." };
                json = j.String;
            }
            catch (JsonException) { return new LoadOutcome { Error = "The stored save is damaged." }; }
            return Apply(json);
        }

        LoadOutcome Apply(string json)
        {
            var prepared = Prepare(json, out var candles);
            if (!prepared.Ok) return new LoadOutcome { Error = prepared.Error };
            SaveCodec.Apply(_world, prepared.Data, candles);
            _lastBodyHash = null;
            return new LoadOutcome { Ok = true };
        }

        public bool HasSave { get { return _storage.Read("current") != null || _storage.Read("backup") != null; } }

        /// <summary>Load the newest save; if it is damaged, fall back to the backup and say so.</summary>
        public LoadOutcome Load()
        {
            var current = _storage.Read("current");
            if (current != null)
            {
                var r = LoadRecord(current);
                if (r.Ok) return r;
                var backup = _storage.Read("backup");
                if (backup != null)
                {
                    var b = LoadRecord(backup);
                    if (b.Ok) { b.Notice = "The newest save was damaged (" + r.Error + ") so the previous one was loaded."; return b; }
                }
                return r;
            }
            var only = _storage.Read("backup");
            if (only != null) return LoadRecord(only);
            return new LoadOutcome { Error = "There is no saved game yet." };
        }

        /// <summary>Load a save file exported from this game or from the web version.</summary>
        public LoadOutcome Import(string text)
        {
            if (text != null && text.Length > 100_000_000) return new LoadOutcome { Error = "The file is too large to be a save." };
            return Apply(text ?? "");
        }

        public LoadOutcome RestoreBackup(string day)
        {
            var record = _storage.Read("daily-" + day);
            return record == null ? new LoadOutcome { Error = "That backup no longer exists." } : LoadRecord(record);
        }
    }
}
