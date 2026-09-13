using System;
using EC2BUnofficialPatch.Resources;
using EC2BUnofficialPatch.Workshop;

namespace EC2BUnofficialPatch.Core
{
    internal sealed class PluginServices : IDisposable
    {
        private PluginServices(
            ContentRootCatalog contentRoots,
            ExternalResourceResolver resourceResolver,
            ResourceIndex resourceIndex,
            ComicResourceIndex comicResources,
            TextureCache textureCache)
        {
            ContentRoots = contentRoots;
            ResourceResolver = resourceResolver;
            ResourceIndex = resourceIndex;
            ComicResources = comicResources;
            TextureCache = textureCache;
        }

        internal ContentRootCatalog ContentRoots { get; }
        internal ExternalResourceResolver ResourceResolver { get; }
        internal ResourceIndex ResourceIndex { get; }
        internal ComicResourceIndex ComicResources { get; }
        internal TextureCache TextureCache { get; }

        internal static PluginServices Create()
        {
            ContentRootCatalog roots = ContentRootCatalog.Discover();
            ExternalResourceResolver resolver = new ExternalResourceResolver(roots);
            ResourceIndex index = ResourceIndex.Build(roots, resolver);
            ComicResourceIndex comics = PluginConfig.ScreenComicExtension.Value
                ? ComicResourceIndex.Build(roots, resolver)
                : ComicResourceIndex.Empty(resolver);
            return new PluginServices(roots, resolver, index, comics, new TextureCache());
        }

        public void Dispose()
        {
            TextureCache.Dispose();
        }
    }
}
