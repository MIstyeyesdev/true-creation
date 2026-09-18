using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// Builds complete DT_PalMonsterParameter rows for brand-new Pals.
    ///
    /// A new row inherits nothing: PalSchema has no shipped row to merge into,
    /// so every field left unset takes the raw struct default (0 / false /
    /// "None"). That is how a custom Pal ends up unable to move or with no
    /// collision. This seeds all 90 fields from the median of real Pals of the
    /// same size, then lets the caller override whichever ones it cares about.
    /// Fields in PalMonsterParameterRow.OptionalFields are left unset so the
    /// row matches the verified working mod's shape.
    /// </summary>
    public static class PalFactory
    {
        private static readonly FieldInfo[] RowFields =
            typeof(PalMonsterParameterRow).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// A complete row seeded from <paramref name="size"/>'s medians.
        ///
        /// <paramref name="bpClass"/> is the DT_PalBPClass ROW KEY the row references. The
        /// working mod gives every new Pal its own row (key = its id) that points at the
        /// base Pal's class path; NewPalBuilder writes that row. Passing an existing Pal's
        /// key instead makes the new row share that Pal's BPClass row (the legacy route).
        /// The display name is NOT written here: vanilla rows carry OverrideNameTextID None
        /// and the game reads PAL_NAME_&lt;id&gt; from the text tables (translations/).
        /// </summary>
        public static PalMonsterParameterRow CreateNew(
            GameData data,
            string name,
            string size,
            string bpClass,
            string tribe = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A new Pal needs a name; it becomes the row key.", nameof(name));
            if (string.IsNullOrWhiteSpace(bpClass))
                throw new ArgumentException(
                    "A new Pal needs a BPClass to borrow a model from, or it has nothing to render.",
                    nameof(bpClass));

            if (!data.SizeDefaultsBySize.TryGetValue(size, out var defaults))
            {
                throw new ArgumentException(
                    $"No defaults for size '{size}'. Known sizes: " +
                    string.Join(", ", data.SizeDefaultsBySize.Keys), nameof(size));
            }

            var row = new PalMonsterParameterRow();

            foreach (var field in RowFields)
            {
                if (PalMonsterParameterRow.OptionalFields.Contains(field.Name)) continue;
                if (!defaults.Fields.TryGetValue(field.Name, out var token)) continue;
                var value = Convert(token, field.FieldType);
                if (value != null) field.SetValue(row, value);
            }

            // Identity is never a median -- it is what makes this Pal itself.
            row.OverrideNameTextID = "None"; // the name is PAL_NAME_<id> in translations/ (vanilla: 323/323 base rows use None)
            row.NamePrefixID = "None";
            row.OverridePartnerSkillNameTextID = "None";
            row.OverridePartnerSkillDescTextID = "None";
            row.IsPal = true;
            row.Tribe = Prefixed("EPalTribeID", string.IsNullOrWhiteSpace(tribe) ? name : tribe);
            row.BPClass = bpClass;
            row.Size = Prefixed("EPalSizeType", size);
            // A Paldex slot is identity, not a size statistic. Copying the size median
            // put every new Medium Pal at 97 (S 60, L 116, XL 130, XS 25), on top of
            // whoever holds that number, so the editor opened on a collision. A new Pal
            // stands at the first unused number with no letter until the editor is told
            // which Pal it is a variant of (then: that number + its next free letter).
            row.ZukanIndex = data.NextFreeZukanIndex();
            row.ZukanIndexSuffix = "";

            // A new Pal should not inherit boss behaviour from whatever the
            // medians happened to produce.
            row.IsBoss = false;
            row.IsTowerBoss = false;
            row.IsRaidBoss = false;
            row.UseBossHPGauge = false;

            // Derived from the capsule rather than averaged: it positions the
            // mesh against its own collision capsule, so an unrelated Pal's
            // offset would sink or float the model.
            var halfHeight = row.MeshCapsuleHalfHeight ?? 100f;
            row.MeshRelativeLocation = new PalVector(0f, 0f, -halfHeight * 0.2f);

            return row;
        }

        /// <summary>
        /// A complete row cloned from a vanilla row (base_rows.json), then given the new
        /// Pal's identity: row key, tribe, own BPClass row key, None text ids, boss flags off.
        /// This is how the verified working mod builds its 50 new Pals (each is a 90-field
        /// copy of a base Pal with edits), so the result is known to load.
        /// </summary>
        public static PalMonsterParameterRow CreateFromBase(JObject baseRow, string name, string tribe, string bpClassRowKey, bool bossRow = false)
        {
            if (baseRow == null) throw new ArgumentNullException(nameof(baseRow));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A new Pal needs a name; it becomes the row key.", nameof(name));
            var row = baseRow.ToObject<PalMonsterParameterRow>(JsonSerializer.Create(PalSchemaMod.SerializerSettings))
                      ?? new PalMonsterParameterRow();
            return ApplyIdentity(row, name, tribe, bpClassRowKey, bossRow);
        }

        /// <summary>
        /// Gives an existing row - a base clone, or the Creation Engine's edited row - the new
        /// Pal's identity: tribe, own BPClass row key, None text ids, IsPal, boss flags off unless
        /// <paramref name="bossRow"/>, and the optional 91st field dropped as the working mod does.
        /// Stats and every other field are left exactly as they are.
        /// </summary>
        public static PalMonsterParameterRow ApplyIdentity(PalMonsterParameterRow row, string name, string tribe, string bpClassRowKey, bool bossRow = false)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A new Pal needs a name; it becomes the row key.", nameof(name));
            row.OverrideNameTextID = "None";
            row.NamePrefixID = "None";
            row.OverridePartnerSkillNameTextID = "None";
            row.OverridePartnerSkillDescTextID = "None";
            row.IsPal = true;
            row.Tribe = Prefixed("EPalTribeID", string.IsNullOrWhiteSpace(tribe) ? name : tribe);
            row.BPClass = string.IsNullOrWhiteSpace(bpClassRowKey) ? name : bpClassRowKey;
            row.ZukanIndexSuffix = row.ZukanIndexSuffix ?? "";
            // The base table carries the optional 91st field; the working mod omits it from every
            // row, so a clone drops it too unless the author sets it on purpose.
            foreach (var optional in PalMonsterParameterRow.OptionalFields)
                typeof(PalMonsterParameterRow).GetField(optional)?.SetValue(row, null);
            if (!bossRow)
            {
                row.IsBoss = false;
                row.IsTowerBoss = false;
                row.IsRaidBoss = false;
                row.UseBossHPGauge = false;
            }
            return row;
        }

        /// <summary>A field-for-field copy of a row (serialize round trip).</summary>
        public static PalMonsterParameterRow Clone(PalMonsterParameterRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var json = JsonConvert.SerializeObject(row, PalSchemaMod.SerializerSettings);
            return JsonConvert.DeserializeObject<PalMonsterParameterRow>(json) ?? new PalMonsterParameterRow();
        }

        /// <summary>
        /// Wraps a bare value in its Unreal enum prefix. "None" is a real
        /// literal for several of these fields, not an enum member, so it is
        /// left alone.
        /// </summary>
        public static string Prefixed(string enumName, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "None") return "None";
            return value.Contains("::") ? value : enumName + "::" + value;
        }

        private static object Convert(JToken token, Type target)
        {
            if (token == null || token.Type == JTokenType.Null) return null;

            try
            {
                if (target == typeof(string)) return token.Value<string>();
                if (target == typeof(bool?)) return token.Value<bool>();
                if (target == typeof(int?)) return token.Value<int>();
                if (target == typeof(float?)) return token.Value<float>();
            }
            catch (FormatException) { return null; }
            catch (InvalidCastException) { return null; }

            return null;
        }

        /// <summary>
        /// Clamps a value to the range actually seen in the shipped data, and
        /// says so when it had to. Out-of-range values are not rejected -- real
        /// mods deliberately exceed them -- but the caller can surface the note
        /// so the user knows they are off the map.
        /// </summary>
        public static bool TryNoteOutOfRange(
            GameData data, string fieldName, double value, out string note)
        {
            note = null;
            var field = data.Field(fieldName);
            if (field?.RealMin == null || field.RealMax == null) return false;

            if (value < field.RealMin.Value || value > field.RealMax.Value)
            {
                note = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} = {1} is outside the range seen in any shipped Pal ({2} to {3}). " +
                    "That is allowed, just untested territory.",
                    fieldName, value, field.RealMin.Value, field.RealMax.Value);
                return true;
            }

            return false;
        }
    }
}
