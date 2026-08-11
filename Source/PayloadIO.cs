using System;
using System.IO;
using Verse;

namespace RimColonyTransfer
{
    /// <summary>
    /// Reads and writes the exchange file through RimWorld's own Scribe, so cross
    /// references inside the payload are resolved by the game rather than by hand.
    /// </summary>
    public static class PayloadIO
    {
        public static string DefaultDirectory =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "ColonyTransfer");

        public static string PathFor(string name) =>
            Path.Combine(DefaultDirectory, name + ColonyPayload.FileExtension);

        public static void Save(ColonyPayload payload, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                Scribe.saver.InitSaving(path, ColonyPayload.RootElement);
                Scribe_Deep.Look(ref payload, "payload");
            }
            catch (Exception ex)
            {
                Log.Error($"[ColonyTransfer] Failed writing payload to {path}: {ex}");
                Scribe.ForceStop();
                throw;
            }
            finally
            {
                if (Scribe.mode == LoadSaveMode.Saving)
                    Scribe.saver.FinalizeSaving();
            }
        }

        /// <summary>
        /// Loading runs a full cross-ref pass. References to anything outside the
        /// payload resolve to null and log a warning — that is expected, not a
        /// failure; the importer reassigns them against the destination game.
        /// </summary>
        public static ColonyPayload Load(string path)
        {
            ColonyPayload payload = null;
            try
            {
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref payload, "payload");
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                Log.Error($"[ColonyTransfer] Failed reading payload from {path}: {ex}");
                Scribe.ForceStop();
                throw;
            }

            if (payload != null && payload.formatVersion != ColonyPayload.CurrentFormatVersion)
            {
                Log.Warning(
                    $"[ColonyTransfer] Payload format version {payload.formatVersion} " +
                    $"differs from current {ColonyPayload.CurrentFormatVersion}.");
            }
            return payload;
        }
    }
}
