using FMT.PluginInterfaces;
using FMT.PluginInterfaces.Assets;
using FMT.ServicesManagers;
using FMT.ServicesManagers.Interfaces;

namespace SkatePlugin
{

    public class SkateAssetLoader : IAssetLoader
    {
        public void LoadData(IEnumerable<string> superBundles, string folder = "native_data/")
        {
            var ffs = SingletonService.GetInstance<IFileSystemService>();
            foreach (var sbName in superBundles)
            {
                var tocFileRAW = $"{folder}{sbName}.toc";
                string tocFileLocation = ffs.ResolvePath(tocFileRAW);
                if (!string.IsNullOrEmpty(tocFileLocation) && File.Exists(tocFileLocation))
                {
                    SkateTOCFile tocFile = new(tocFileRAW, true, true, false, -1, false);
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                }
            }
        }

        public IEnumerable<IAssetEntry> Load(IEnumerable<string> superBundles)
        {
            var fss = SingletonService.GetInstance<IFileSystemService>();
            fss.TOCFileType = typeof(SkateTOCFile);
            LoadData(superBundles, "native_patch/");
            LoadData(superBundles);
            return null;
        }
    }


}
