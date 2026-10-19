using FMT.FileTools;
using FMT.Logging;
using FMT.Models.Assets.AssetEntry.Entries;
using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;
using System.Text;

namespace SkatePlugin.Cache
{
    public class SkateCacheReader : ICacheReader
    {
        protected ILogger Logger { get; set; }

        // NOT NEEDED
        public ulong EbxDataOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // NOT NEEDED
        public ulong ResDataOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // NOT NEEDED
        public ulong ChunkDataOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // NOT NEEDED
        public ulong NameToPositionOffset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <summary>
        /// Reads and processes data from the Madden 26 cache file, populating asset management services with the
        /// extracted data.
        /// </summary>
        /// <remarks>This method reads various asset entries, including bundles, EBX assets, resources,
        /// and chunks, from the cache file. The extracted data is added to the asset management service if available.
        /// Progress updates are logged periodically.</remarks>
        /// <param name="logger">The logger used to record progress and status messages during the read operation.</param>
        /// <returns><see langword="true"/> if the cache file matches the expected system iteration and no patching is required;
        /// otherwise, <see langword="false"/>.</returns>
        public bool Read(ILogger logger)
        {
            Logger = logger;
            var fss = SingletonService.GetInstance<IFileSystemService>();
            var assetManagementService = SingletonService.GetInstance<IAssetManagementService>();
            var cacheHelpers = new SkateCacheHelpers();

            if (!File.Exists(cacheHelpers.GetCachePath()))
                return false;

            bool patched = false;
            using (NativeReader nativeReader = new NativeReader(new FileStream(cacheHelpers.GetCachePath(), FileMode.Open, FileAccess.Read)))
            {
                if (nativeReader.ReadLengthPrefixedString() != "fc26")
                    return false;

                var cacheHead = nativeReader.ReadULong();
                if (cacheHead != cacheHelpers.GetSystemIteration())
                    patched = true;

                logger.Log("Cache: Reading bundles");
                int count = 0;
                // bundle count
                count = nativeReader.ReadInt();
                for (int k = 0; k < count; k++)
                {
                    if (k % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)k / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading bundles [{pct}%]");
                    }

                    BundleEntry bE = new();
                    var nameLength = nativeReader.ReadUShort();
                    bE.Name = Encoding.UTF8.GetString(nativeReader.ReadBytes(nameLength));
                    bE.SuperBundleId = nativeReader.ReadInt();

                    if (assetManagementService != null && assetManagementService.Bundles.FindIndex(x => x.Name.Length == bE.Name.Length && x.Name == bE.Name) == -1)
                        assetManagementService.Bundles.Add(bE);
                }

                logger.Log("Cache: Reading Ebx");
                count = nativeReader.ReadInt();
                for (int k = 0; k < count; k++)
                {
                    if (k % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)k / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading Ebx [{pct}%]");
                    }

                    var asset = ReadEbxAssetEntry(nativeReader);

                    if (assetManagementService != null)
                        assetManagementService.AddEbx(asset as EbxAssetEntry);

                }

                logger.Log("Cache: Reading Resources");
                count = nativeReader.ReadInt();
                for (int k = 0; k < count; k++)
                {
                    if (k % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)k / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading Resources [{pct}%]");
                    }
                    var asset = ReadResAssetEntry(nativeReader);

                    if (assetManagementService != null)
                        assetManagementService.AddRes(asset as ResAssetEntry);
                }

                // ------------------------------------------------------------------------
                // Chunks
                logger.Log("Cache: Reading Chunks");
                count = nativeReader.ReadInt();
                for (int chunkIndex = 0; chunkIndex < count; chunkIndex++)
                {
                    if (chunkIndex % 100 == 0)
                    {
                        var pct = (int)Math.Round(((double)chunkIndex / count) * 100);
                        logger.LogProgress(pct);
                        logger.Log($"Cache: Reading Chunks [{pct}%]");
                    }

                    var asset = ReadChunkAssetEntry(nativeReader);

                    if (assetManagementService != null)
                        assetManagementService.AddChunk(asset as ChunkAssetEntry);
                }

                // ------------------------------------------------------------------------
                // Chunks in Bundles
                logger.Log("Cache: Reading Chunks in Bundles");
                count = nativeReader.ReadInt();
                for (int chunkIndex = 0; chunkIndex < count; chunkIndex++)
                {
                    if (chunkIndex % 100 == 0)
                    {
                        logger.LogProgress((int)Math.Round(((double)chunkIndex / count) * 100));
                    }
                    var chunkAssetEntry = ReadChunkAssetEntry(nativeReader);
                    chunkAssetEntry.IsTocChunk = true;

                    if (assetManagementService != null)
                        assetManagementService.AddChunk(chunkAssetEntry as ChunkAssetEntry);
                }
            }
            return !patched;
        }

        public virtual IEbxAssetEntry ReadEbxAssetEntry(NativeReader nativeReader)
        {
            EbxAssetEntry ebxAssetEntry = new();
            ebxAssetEntry.Name = nativeReader.ReadLengthPrefixedString();
            ebxAssetEntry.Sha1 = nativeReader.ReadSha1();
            ebxAssetEntry.BaseSha1 = ebxAssetEntry.Sha1;
            ebxAssetEntry.Size = nativeReader.ReadLong();
            ebxAssetEntry.OriginalSize = nativeReader.ReadLong();
            ebxAssetEntry.Location = (AssetDataLocation)nativeReader.ReadInt();
            ebxAssetEntry.Type = nativeReader.ReadLengthPrefixedString();
            ebxAssetEntry.Id = nativeReader.ReadGuid();
            if (nativeReader.ReadBoolean())
            {
                ebxAssetEntry.ExtraData = new AssetExtraData();
                ebxAssetEntry.ExtraData.DataOffset = nativeReader.ReadUInt();
                ebxAssetEntry.ExtraData.Catalog = nativeReader.ReadUShort();
                ebxAssetEntry.ExtraData.Cas = nativeReader.ReadUShort();
                ebxAssetEntry.ExtraData.IsPatch = nativeReader.ReadBoolean();
            }

            if (nativeReader.ReadBoolean())
                ebxAssetEntry.TOCFileLocation = nativeReader.ReadLengthPrefixedString();

            ebxAssetEntry.SB_CAS_Offset_Position = nativeReader.ReadInt();
            ebxAssetEntry.SB_CAS_Size_Position = nativeReader.ReadInt();
            ebxAssetEntry.SB_Sha1_Position = nativeReader.ReadInt();
            ebxAssetEntry.SB_OriginalSize_Position = nativeReader.ReadInt();

            int bundleCount = nativeReader.ReadInt();
            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
            {
                ebxAssetEntry.Bundles.Add(nativeReader.ReadInt());
            }

            return ebxAssetEntry;
        }

        public virtual IResourceAssetEntry ReadResAssetEntry(NativeReader nativeReader)
        {
            ResAssetEntry resAssetEntry = new();
            resAssetEntry.Name = nativeReader.ReadLengthPrefixedString();
            resAssetEntry.Sha1 = nativeReader.ReadSha1();
            resAssetEntry.BaseSha1 = resAssetEntry.Sha1;
            resAssetEntry.Size = nativeReader.ReadLong();
            resAssetEntry.OriginalSize = nativeReader.ReadLong();
            resAssetEntry.Location = (AssetDataLocation)nativeReader.ReadInt();
            resAssetEntry.IsInline = nativeReader.ReadBoolean();
            resAssetEntry.ResRid = nativeReader.ReadULong();
            resAssetEntry.ResType = nativeReader.ReadUInt();
            resAssetEntry.ResMeta = nativeReader.ReadBytes(nativeReader.ReadInt());
            if (nativeReader.ReadBoolean())
            {
                resAssetEntry.ExtraData = new AssetExtraData();
                resAssetEntry.ExtraData.DataOffset = nativeReader.ReadUInt();
                resAssetEntry.ExtraData.Catalog = nativeReader.ReadUShort();
                resAssetEntry.ExtraData.Cas = nativeReader.ReadUShort();
                resAssetEntry.ExtraData.IsPatch = nativeReader.ReadBoolean();
            }

            if (nativeReader.ReadBoolean())
                resAssetEntry.TOCFileLocation = nativeReader.ReadLengthPrefixedString();

            resAssetEntry.SB_CAS_Offset_Position = nativeReader.ReadInt();
            resAssetEntry.SB_CAS_Size_Position = nativeReader.ReadInt();
            resAssetEntry.SB_Sha1_Position = nativeReader.ReadInt();
            resAssetEntry.SB_OriginalSize_Position = nativeReader.ReadInt();

            int bundleCount = nativeReader.ReadInt();
            for (int bundleIndex = 0; bundleIndex < bundleCount; bundleIndex++)
            {
                resAssetEntry.Bundles.Add(nativeReader.ReadInt());
            }

            return resAssetEntry;
        }

        public virtual IChunkAssetEntry ReadChunkAssetEntry(NativeReader nativeReader)
        {
            ChunkAssetEntry chunkAssetEntry = new();
            chunkAssetEntry.Id = nativeReader.ReadGuid();
            chunkAssetEntry.Sha1 = nativeReader.ReadSha1();
            chunkAssetEntry.BaseSha1 = chunkAssetEntry.Sha1;
            chunkAssetEntry.Size = nativeReader.ReadLong();
            chunkAssetEntry.Location = (AssetDataLocation)nativeReader.ReadInt();
            chunkAssetEntry.IsInline = nativeReader.ReadBoolean();
            chunkAssetEntry.BundledSize = nativeReader.ReadUInt();
            chunkAssetEntry.RangeStart = nativeReader.ReadUInt();
            chunkAssetEntry.RangeEnd = nativeReader.ReadUInt();
            chunkAssetEntry.LogicalOffset = nativeReader.ReadUInt();
            chunkAssetEntry.LogicalSize = nativeReader.ReadUInt();
            chunkAssetEntry.H32 = nativeReader.ReadInt();
            chunkAssetEntry.FirstMip = nativeReader.ReadInt();
            if (nativeReader.ReadBoolean())
            {
                chunkAssetEntry.ExtraData = new AssetExtraData();
                chunkAssetEntry.ExtraData.DataOffset = nativeReader.ReadUInt();
                chunkAssetEntry.ExtraData.Catalog = nativeReader.ReadUShort();
                chunkAssetEntry.ExtraData.Cas = nativeReader.ReadUShort();
                chunkAssetEntry.ExtraData.IsPatch = nativeReader.ReadBoolean();
                chunkAssetEntry.Location = AssetDataLocation.CasNonIndexed;
            }

            if (nativeReader.ReadBoolean())
                chunkAssetEntry.TOCFileLocation = nativeReader.ReadLengthPrefixedString();

            chunkAssetEntry.SB_CAS_Offset_Position = nativeReader.ReadInt();
            chunkAssetEntry.SB_CAS_Size_Position = nativeReader.ReadInt();
            chunkAssetEntry.SB_Sha1_Position = nativeReader.ReadInt();
            chunkAssetEntry.SB_OriginalSize_Position = nativeReader.ReadInt();

            chunkAssetEntry.SB_LogicalOffset_Position = nativeReader.ReadUInt();
            chunkAssetEntry.SB_LogicalSize_Position = nativeReader.ReadUInt();

            int bundleCount = nativeReader.ReadInt();
            for (int i = 0; i < bundleCount; i++)
            {
                chunkAssetEntry.Bundles.Add(nativeReader.ReadInt());
            }

            return chunkAssetEntry;
        }
    }
}
